using System.Text;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Application.Widgets.Icons;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Ui.Resources;

[TestFixture]
internal sealed class SizedIconResourceTests
{
	private PluginIconPackTestHost _host = null!;
	private IWidgetIconSource _iconPackSource = null!;
	private PluginIconUiResources _pluginIcons = null!;

	[SetUp]
	public void SetUp()
	{
		_host = new PluginIconPackTestHost();
		_iconPackSource = new IconPackWidgetIconSource(_host.Icons.Cache, _host.ScopeFactory);
		_pluginIcons = new PluginIconUiResources(_host.Icons.Cache,
			_host.Resolver,
			new WidgetIconSourceRegistry([_iconPackSource]));
	}

	[TearDown]
	public void TearDown() => _host.Dispose();

	[TestCase("128", 128)]
	[TestCase("512", 512)]
	[TestCase("700", 512)]
	[TestCase("100", 128)]
	public async Task A_plugin_icon_is_served_at_the_requested_resolution(string size, int expectedEdge)
	{
		var handle = await LargeIcon(1024);

		var controller = Controller();
		var served = await controller.Get(handle.ResourceId, handle.ContentHash, size) as FileContentResult;

		Assert.That(EdgeOf(served), Is.EqualTo(expectedEdge));
	}

	[TestCase(null)]
	[TestCase("0")]
	[TestCase("-5")]
	[TestCase("abc")]
	[TestCase("200")]
	[TestCase("256")]
	public async Task Without_a_usable_size_the_default_rendition_is_served_exactly_as_before(string? size)
	{
		var handle = await LargeIcon(1024);

		var controller = Controller();
		var served = await controller.Get(handle.ResourceId, handle.ContentHash, size) as FileContentResult;

		Assert.Multiple(() =>
		{
			Assert.That(EdgeOf(served), Is.EqualTo(256));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.EqualTo($"\"{handle.ContentHash}\""));
			Assert.That(controller.Response.Headers.CacheControl.ToString(),
				Is.EqualTo("private, max-age=31536000, immutable"));
		});
	}

	[Test]
	public async Task Each_resolution_has_its_own_etag()
	{
		var handle = await LargeIcon(1024);

		var small = Controller();
		await small.Get(handle.ResourceId, handle.ContentHash, "128");
		var large = Controller();
		await large.Get(handle.ResourceId, handle.ContentHash, "512");

		var etags = new[]
		{
			small.Response.Headers.ETag.ToString(), large.Response.Headers.ETag.ToString(), $"\"{handle.ContentHash}\""
		};
		Assert.That(etags, Is.Unique);
	}

	[Test]
	public async Task A_validator_for_one_resolution_does_not_validate_another()
	{
		var handle = await LargeIcon(1024);
		var small = Controller();
		await small.Get(handle.ResourceId, handle.ContentHash, "128");

		var large = Controller(ifNoneMatch: small.Response.Headers.ETag.ToString());
		var served = await large.Get(handle.ResourceId, handle.ContentHash, "512") as FileContentResult;

		Assert.That(EdgeOf(served), Is.EqualTo(512));
	}

	[Test]
	public async Task A_revalidated_resolution_answers_not_modified()
	{
		var handle = await LargeIcon(1024);
		var first = Controller();
		await first.Get(handle.ResourceId, handle.ContentHash, "512");

		var again = Controller(ifNoneMatch: first.Response.Headers.ETag.ToString());
		var result = await again.Get(handle.ResourceId, handle.ContentHash, "512");

		Assert.That(result, Is.InstanceOf<StatusCodeResult>().With.Property("StatusCode").EqualTo(304));
	}

	[Test]
	public async Task A_small_icon_is_never_upscaled()
	{
		var handle = await LargeIcon(100);

		var served = await Controller().Get(handle.ResourceId, handle.ContentHash, "512") as FileContentResult;

		Assert.That(EdgeOf(served), Is.EqualTo(100));
	}

	[Test]
	public async Task A_sized_rendition_is_cached_for_good_only_under_the_current_version()
	{
		var handle = await LargeIcon(1024);

		var current = Controller();
		await current.Get(handle.ResourceId, handle.ContentHash, "128");
		var stale = Controller();
		var staleServed = await stale.Get(handle.ResourceId, "sha256:stale", "128") as FileContentResult;
		var unversioned = Controller();
		await unversioned.Get(handle.ResourceId, null, "128");

		Assert.Multiple(() =>
		{
			Assert.That(current.Response.Headers.CacheControl.ToString(), Is.EqualTo("private, max-age=31536000, immutable"));
			Assert.That(stale.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
			Assert.That(EdgeOf(staleServed), Is.EqualTo(128));
			Assert.That(unversioned.Response.Headers.CacheControl.ToString(), Is.EqualTo("private, no-cache"));
		});
	}

	[Test]
	public async Task A_widget_icon_resource_is_served_at_the_requested_resolution()
	{
		var pack = await _host.Icons.CreatePack();
		var icon = await _host.Icons.AddReadyIcon(pack.Id, "home", await Png(1024));
		var store = new UiResourceStore();
		var widgetIcons = new WidgetIconResources(new WidgetIconSourceRegistry([_iconPackSource]),
			store,
			Serilog.Core.Logger.None);
		var handle = await widgetIcons.ResolveAsync(WidgetIconReference.IconPack(icon.Id.ToString()), CancellationToken.None);

		var served = await Controller(store).Get(handle!.ResourceId, handle.ContentHash, "128") as FileContentResult;

		Assert.That(EdgeOf(served), Is.EqualTo(128));
	}

	[Test]
	public async Task A_resource_that_is_not_an_icon_ignores_the_size()
	{
		var store = new UiResourceStore();
		var handle = store.Register(new UiResourceRegistration
		{
			OwnerId = "app.macro-deck.weather",
			Name = "clear-day",
			MediaType = "image/svg+xml",
			Content = Encoding.UTF8.GetBytes("""<svg xmlns="http://www.w3.org/2000/svg" width="1" height="1"/>""")
		});

		var controller = Controller(store);
		var served = await controller.Get(handle.ResourceId, handle.ContentHash, "128") as FileContentResult;

		Assert.Multiple(() =>
		{
			Assert.That(served!.ContentType, Is.EqualTo("image/svg+xml"));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.EqualTo($"\"{handle.ContentHash}\""));
		});
	}

	[Test]
	public async Task A_degraded_rendition_is_never_cached()
	{
		var source = new FakeSource { Stable = false };
		var store = new UiResourceStore();
		var handle = await RegisterFake(store, source);

		var controller = Controller(store, sources: [source]);
		var served = await controller.Get(handle.ResourceId, handle.ContentHash, "512");

		Assert.Multiple(() =>
		{
			Assert.That(served, Is.InstanceOf<FileContentResult>());
			Assert.That(controller.Response.Headers.CacheControl.ToString(), Is.EqualTo("no-store"));
		});
	}

	[Test]
	public async Task An_icon_too_large_at_the_requested_resolution_still_yields_one_that_fits()
	{
		var source = new FakeSource { OversizedAtOrAbove = 512 };
		var store = new UiResourceStore();
		var handle = await RegisterFake(store, source);

		var served = await Controller(store, sources: [source]).Get(handle.ResourceId, handle.ContentHash, "512")
			as FileContentResult;

		Assert.That(EdgeOf(served), Is.EqualTo(256));
	}

	private async Task<MacroDeck.Ui.Model.Resources.UiResource> LargeIcon(int edge)
	{
		var pack = await _host.Icons.CreatePack();
		var icon = await _host.Icons.AddReadyIcon(pack.Id, "home", await Png(edge));
		var handle = await _pluginIcons.GetHandleAsync(icon.Id, CancellationToken.None);
		return handle.Handle!;
	}

	private static async Task<MacroDeck.Ui.Model.Resources.UiResource> RegisterFake(UiResourceStore store, FakeSource source)
	{
		var widgetIcons = new WidgetIconResources(new WidgetIconSourceRegistry([source]), store, Serilog.Core.Logger.None);
		return (await widgetIcons.ResolveAsync(new WidgetIconReference(FakeSource.SourceType, "logo"), CancellationToken.None))!;
	}

	private UiResourcesController Controller(IUiResourceStore? store = null,
		string? ifNoneMatch = null,
		IWidgetIconSource[]? sources = null)
	{
		var httpContext = new DefaultHttpContext();
		if (ifNoneMatch is not null)
		{
			httpContext.Request.Headers.IfNoneMatch = ifNoneMatch;
		}

		return new UiResourcesController(store ?? new UiResourceStore(),
			_pluginIcons,
			new IconUiResourceRenditions(sources ?? [_iconPackSource], _host.Icons.Cache))
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext }
		};
	}

	private static int EdgeOf(FileContentResult? served)
	{
		Assert.That(served, Is.Not.Null);
		var info = Image.Identify(served!.FileContents);
		return Math.Max(info.Width, info.Height);
	}

	private static async Task<byte[]> Png(int edge)
	{
		using var image = new Image<Rgba32>(edge, edge, new Rgba32(200, 40, 40, 255));
		using var buffer = new MemoryStream();
		await image.SaveAsPngAsync(buffer);
		return buffer.ToArray();
	}

	private sealed class FakeSource : IWidgetIconSource
	{
		public const string SourceType = "test";

		public bool Stable { get; init; } = true;

		public int OversizedAtOrAbove { get; init; } = int.MaxValue;

		public string Type => SourceType;

		public string? GetVersion(string reference) => "v1";

		public async Task<WidgetIconImage?> GetImageAsync(string reference,
			int size,
			bool acceptWebp,
			bool staticFrame,
			CancellationToken cancellationToken)
		{
			var bytes = size >= OversizedAtOrAbove
				? new byte[MacroDeck.Plugin.Protocol.Limits.ProtocolLimits.MaxUiResourceBytes + 1]
				: await Png(size);
			return new WidgetIconImage(new MemoryStream(bytes), "image/png", Stable);
		}
	}
}
