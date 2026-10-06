using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Ui.Resources;

[TestFixture]
internal sealed class IconAppearanceResourceTests
{
	private static readonly Rgba32 _lightColor = new(250, 250, 250, 255);
	private static readonly Rgba32 _darkColor = new(10, 10, 10, 255);

	private PluginIconPackTestHost _host = null!;
	private IWidgetIconSource _iconPackSource = null!;
	private PluginIconUiResources _pluginIcons = null!;
	private UiResourceStore _store = null!;
	private WidgetIconResources _widgetIcons = null!;

	[SetUp]
	public void SetUp()
	{
		_host = new PluginIconPackTestHost();
		_iconPackSource = new IconPackWidgetIconSource(_host.Icons.Cache, _host.ScopeFactory);
		_pluginIcons = new PluginIconUiResources(_host.Icons.Cache,
			_host.Resolver,
			new WidgetIconSourceRegistry([_iconPackSource]));
		_store = new UiResourceStore();
		_widgetIcons = new WidgetIconResources(new WidgetIconSourceRegistry([_iconPackSource]),
			_store,
			Serilog.Core.Logger.None,
			_host.Icons.Cache);
	}

	[TearDown]
	public void TearDown() => _host.Dispose();

	[Test]
	public async Task A_plain_icon_keeps_its_resource_id_version_and_etag()
	{
		var icon = await Icon(_lightColor);
		var withoutCatalog = new WidgetIconResources(new WidgetIconSourceRegistry([_iconPackSource]),
			new UiResourceStore(),
			Serilog.Core.Logger.None,
			new MacroDeckHost.Tests.UnitTests.Devices.Surfaces.StubIconPackCache());
		var reference = WidgetIconReference.IconPack(icon.Id.ToString());
		var iconService = IconService();

		var resource = await _widgetIcons.ResolveAsync(reference, CancellationToken.None);
		var before = await withoutCatalog.ResolveAsync(reference, CancellationToken.None);
		var plainImage = await iconService.GetImage(icon.Id, null, true, false, CancellationToken.None);
		var contextImage = await iconService.GetImage(icon.Id,
			null,
			true,
			false,
			CancellationToken.None,
			new IconAppearanceContext("dark", "static"));

		Assert.Multiple(() =>
		{
			Assert.That(resource!.ResourceId, Is.EqualTo(before!.ResourceId).And.EndsWith(icon.Id.ToString()));
			Assert.That(resource.ContentHash, Is.EqualTo(before.ContentHash));
			Assert.That(IconMapper.ToDto(icon, _host.Icons.Cache).ContentHash, Is.EqualTo(icon.MasterContentHash));
			Assert.That(contextImage.Data!.ETag, Is.EqualTo(plainImage.Data!.ETag));
			Assert.That(contextImage.Data.Version, Is.EqualTo(icon.MasterContentHash));
		});
	}

	[Test]
	public async Task An_icon_with_appearances_is_marked_and_its_version_follows_them()
	{
		var icon = await Icon(_lightColor);
		var reference = WidgetIconReference.IconPack(icon.Id.ToString());
		var plain = await _widgetIcons.ResolveAsync(reference, CancellationToken.None);

		var dark = await Appearance(icon, "colorScheme=dark", _darkColor, IconProcessingState.Pending);
		_widgetIcons.Evict(icon.Id);
		var pending = await _widgetIcons.ResolveAsync(reference, CancellationToken.None);

		dark.ProcessingState = IconProcessingState.Ready;
		await _host.Icons.Cache.UpdateIcon(dark);
		_widgetIcons.Evict(icon.Id);
		var ready = await _widgetIcons.ResolveAsync(reference, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(pending!.ResourceId, Is.EqualTo(plain!.ResourceId + ".a"));
			Assert.That(ready!.ResourceId, Is.EqualTo(pending.ResourceId));
			Assert.That(new[] { plain.ContentHash, pending.ContentHash, ready.ContentHash }, Is.Unique);
		});
	}

	[Test]
	public async Task An_appearance_resource_serves_what_the_viewers_context_selects()
	{
		var icon = await Icon(_lightColor);
		await Appearance(icon, "colorScheme=dark", _darkColor);
		var resource = (await _widgetIcons.ResolveAsync(WidgetIconReference.IconPack(icon.Id.ToString()),
			CancellationToken.None))!;

		var dark = await Serve(resource, "?colorScheme=dark&motion=animated");
		var darkSmall = await Serve(resource, "?colorScheme=dark&motion=animated", size: "128");
		var light = await Serve(resource, "?colorScheme=light&motion=animated");
		var noContext = await Serve(resource, string.Empty);

		Assert.Multiple(() =>
		{
			Assert.That(ColorOf(dark), Is.EqualTo(_darkColor));
			Assert.That(ColorOf(darkSmall), Is.EqualTo(_darkColor));
			Assert.That(EdgeOf(darkSmall), Is.EqualTo(128));
			Assert.That(ColorOf(light), Is.EqualTo(_lightColor));
			Assert.That(ColorOf(noContext), Is.EqualTo(_lightColor));
		});
	}

	[Test]
	public async Task A_pinned_appearance_resolves_to_a_resource_of_its_own_that_serves_sized_requests()
	{
		var icon = await Icon(_lightColor);
		var dark = await Appearance(icon, "colorScheme=dark", _darkColor);
		var reference = WidgetIconReference.IconPack(icon.Id.ToString());

		var pinned = (await _widgetIcons.ResolveAsync(reference with { Appearance = "colorScheme=dark" },
			CancellationToken.None))!;
		var pinnedDefault = await _widgetIcons.ResolveAsync(reference with { Appearance = "default" },
			CancellationToken.None);
		var missingKind = await _widgetIcons.ResolveAsync(reference with { Appearance = "colorScheme=light" },
			CancellationToken.None);
		var served = await Serve(pinned, string.Empty, size: "128");

		Assert.Multiple(() =>
		{
			Assert.That(pinned.ResourceId, Is.EqualTo($"{WidgetIconResources.OwnerId}.icon-pack.{dark.Id}"));
			Assert.That(pinnedDefault!.ResourceId, Is.EqualTo($"{WidgetIconResources.OwnerId}.icon-pack.{icon.Id}"));
			Assert.That(missingKind!.ResourceId, Is.EqualTo($"{WidgetIconResources.OwnerId}.icon-pack.{icon.Id}.a"));
			Assert.That(EdgeOf(served), Is.EqualTo(128));
			Assert.That(ColorOf(served), Is.EqualTo(_darkColor));
		});
	}

	[Test]
	public async Task A_plugin_icon_handle_is_marked_and_changes_version_when_an_appearance_is_replaced()
	{
		var icon = await Icon(_lightColor);
		var plain = (await _pluginIcons.GetHandleAsync(icon.Id, CancellationToken.None)).Handle!;

		var first = await Appearance(icon, "colorScheme=dark", _darkColor);
		var withDark = (await _pluginIcons.GetHandleAsync(icon.Id, CancellationToken.None)).Handle!;
		await _host.Icons.Cache.RemoveIcon(first.Id);
		await Appearance(icon, "colorScheme=dark", new Rgba32(60, 0, 0, 255));
		var replaced = (await _pluginIcons.GetHandleAsync(icon.Id, CancellationToken.None)).Handle!;

		Assert.Multiple(() =>
		{
			Assert.That(plain.ResourceId, Is.EqualTo(PluginIconReferences.ResourceId(icon.Id)));
			Assert.That(withDark.ResourceId, Is.EqualTo(plain.ResourceId + ".a"));
			Assert.That(PluginIconReferences.TryParseResourceId(withDark.ResourceId, out var parsed), Is.True);
			Assert.That(parsed, Is.EqualTo(icon.Id));
			Assert.That(new[] { plain.ContentHash, withDark.ContentHash, replaced.ContentHash }, Is.Unique);
		});
	}

	[Test]
	public async Task A_plugin_icon_handle_with_appearances_serves_the_dark_one_to_a_dark_viewer_and_the_default_without_context()
	{
		var icon = await Icon(_lightColor);
		await Appearance(icon, "colorScheme=dark", _darkColor);
		var handle = (await _pluginIcons.GetHandleAsync(icon.Id, CancellationToken.None)).Handle!;

		var dark = await Serve(handle, "?colorScheme=dark");
		var noContext = await Serve(handle, string.Empty);

		Assert.Multiple(() =>
		{
			Assert.That(handle.ResourceId, Does.EndWith(".a"));
			Assert.That(ColorOf(dark), Is.EqualTo(_darkColor));
			Assert.That(ColorOf(noContext), Is.EqualTo(_lightColor));
		});
	}

	[Test]
	public async Task The_icon_image_endpoint_serves_the_appearance_its_query_selects()
	{
		var icon = await Icon(_lightColor);
		await Appearance(icon, "motion=static", _darkColor);
		var version = IconMapper.ToDto(icon, _host.Icons.Cache).ContentHash;

		var still = await FetchImage(icon.Id, version, "?colorScheme=light&motion=static");
		var moving = await FetchImage(icon.Id, version, "?colorScheme=light&motion=animated");

		Assert.Multiple(() =>
		{
			Assert.That(ColorOf(still.Result), Is.EqualTo(_darkColor));
			Assert.That(ColorOf(moving.Result), Is.EqualTo(_lightColor));
			Assert.That(still.ETag, Is.Not.EqualTo(moving.ETag));
			Assert.That(still.CacheControl, Does.Contain("immutable"));
		});
	}

	private IconService IconService()
		=> new(_host.Icons.Cache,
			_host.Icons.Storage,
			_host.Icons.FallbackStore,
			_host.Icons.VariantDeriver,
			_host.Icons.Coalescer,
			_host.Icons.Mediator,
			new IconPackOwnerRegistry([]));

	private async Task<IconEntity> Icon(Rgba32 color)
	{
		var pack = _host.Icons.Cache.GetAllPacks().FirstOrDefault() ?? await _host.Icons.CreatePack();
		return await _host.Icons.AddReadyIcon(pack.Id, "lamp", Png(color));
	}

	private async Task<IconEntity> Appearance(IconEntity parent,
		string key,
		Rgba32 color,
		IconProcessingState state = IconProcessingState.Ready)
	{
		Assert.That(IconAppearanceTraits.TryParse(key, out var traits), Is.True);
		var master = Png(color);
		var asset = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = parent.PackId,
			Name = parent.Name,
			SourceContentHash = SourceContentHash.Compute(master).Value,
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = state,
			AppearanceOfId = parent.Id,
			AppearanceTraits = traits,
			CreatedAt = DateTime.UtcNow
		};
		await _host.Icons.Storage.WriteVariant(parent.PackId,
			asset.Id,
			IconVariants.Master,
			master,
			CancellationToken.None);
		await _host.Icons.Cache.AddIcons(parent.PackId, [asset]);
		return asset;
	}

	private async Task<FileContentResult?> Serve(UiResource resource, string query, string? size = null)
	{
		var controller = new UiResourcesController(_store,
			_pluginIcons,
			new IconUiResourceRenditions([_iconPackSource], _host.Icons.Cache))
		{
			ControllerContext = new ControllerContext { HttpContext = Context(query) }
		};

		return await controller.Get(resource.ResourceId, resource.ContentHash, size) as FileContentResult;
	}

	private async Task<(FileStreamResult? Result, string ETag, string CacheControl)> FetchImage(Guid iconId,
		string? version,
		string query)
	{
		var controller = new IconsController(null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			null!,
			IconService(),
			null!,
			null!,
			_host.Icons.Cache)
		{
			ControllerContext = new ControllerContext { HttpContext = Context(query) }
		};

		var result = await controller.GetImage(iconId.ToString(), null, version, CancellationToken.None);
		return (result as FileStreamResult,
			controller.Response.Headers.ETag.ToString(),
			controller.Response.Headers.CacheControl.ToString());
	}

	private static DefaultHttpContext Context(string query)
	{
		var context = new DefaultHttpContext();
		context.Request.QueryString = new QueryString(query.Length == 0 ? null : query);
		context.Request.Headers.Accept = "image/webp,image/png";
		return context;
	}

	private static Rgba32 ColorOf(FileContentResult? served)
	{
		Assert.That(served, Is.Not.Null);
		using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(served!.FileContents);
		return image[image.Width / 2, image.Height / 2];
	}

	private static Rgba32 ColorOf(FileStreamResult? served)
	{
		Assert.That(served, Is.Not.Null);
		using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(served!.FileStream);
		return image[image.Width / 2, image.Height / 2];
	}

	private static int EdgeOf(FileContentResult? served)
	{
		Assert.That(served, Is.Not.Null);
		var info = SixLabors.ImageSharp.Image.Identify(served!.FileContents);
		return Math.Max(info.Width, info.Height);
	}

	private static byte[] Png(Rgba32 color)
	{
		using var image = new Image<Rgba32>(1024, 1024, color);
		using var buffer = new MemoryStream();
		image.SaveAsPng(buffer);
		return buffer.ToArray();
	}
}
