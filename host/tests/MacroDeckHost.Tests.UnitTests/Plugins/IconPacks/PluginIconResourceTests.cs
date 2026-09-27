using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Plugins.IconPacks;
using MacroDeckHost.Application.Ui.Resources;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Tests.UnitTests.Plugins.IconPacks;

[TestFixture]
internal sealed class PluginIconResourceTests
{
	private const string PluginId = "com.example.logos";

	private PluginIconPackTestHost _host = null!;

	[SetUp]
	public void SetUp() => _host = new PluginIconPackTestHost();

	[TearDown]
	public void TearDown() => _host.Dispose();

	[Test]
	public async Task A_plugin_icon_handle_is_served_by_the_resource_endpoint_from_a_fresh_store_and_is_not_webp()
	{
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "green"))));
		var handle = await _host.UiResources.GetHandleAsync(PluginId, "logos", "Spotify", CancellationToken.None);

		var controller = new UiResourcesController(new UiResourceStore(),
			new PluginIconUiResources(_host.Icons.Cache, _host.Resolver, WidgetIconSources()))
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
		};
		var served = await controller.Get(handle.Handle!.ResourceId, handle.Handle.ContentHash) as FileContentResult;

		Assert.Multiple(() =>
		{
			Assert.That(handle.Status, Is.EqualTo(PluginIconResourceStatus.Found));
			Assert.That(handle.Handle.ResourceId, Does.StartWith(PluginIconReferences.ResourceOwnerId + "."));
			Assert.That(served, Is.Not.Null);
			Assert.That(served!.ContentType, Is.Not.EqualTo("image/webp"));
			Assert.That(served.FileContents.Take(4), Is.Not.EqualTo("RIFF"u8.ToArray()));
			Assert.That(controller.Response.Headers.ETag.ToString(), Is.EqualTo($"\"{handle.Handle.ContentHash}\""));
		});
	}

	[Test]
	public async Task A_replaced_icon_is_handed_out_with_a_new_content_hash()
	{
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "green"))));
		var before = await _host.UiResources.GetHandleAsync(PluginId, "logos", "spotify", CancellationToken.None);

		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "black"))));
		var after = await _host.UiResources.GetHandleAsync(PluginId, "logos", "spotify", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(after.Handle!.ResourceId, Is.EqualTo(before.Handle!.ResourceId));
			Assert.That(after.Handle.ContentHash, Is.Not.EqualTo(before.Handle.ContentHash));
		});
	}

	[Test]
	public async Task Another_plugins_icon_and_an_unknown_name_are_not_found()
	{
		await _host.SyncDevelopment(PluginId, ("logos", await _host.BuildArchive("Logos", ("spotify", "green"))));

		var foreign = await _host.UiResources.GetHandleAsync("com.example.other", "logos", "spotify", CancellationToken.None);
		var unknown = await _host.UiResources.GetHandleAsync(PluginId, "logos", "tidal", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(foreign.Status, Is.EqualTo(PluginIconResourceStatus.NotFound));
			Assert.That(unknown.Status, Is.EqualTo(PluginIconResourceStatus.NotFound));
		});
	}

	[Test]
	public async Task An_icon_of_an_ordinary_pack_is_not_served_under_the_plugin_icon_owner()
	{
		var pack = await _host.Icons.CreatePack();
		var icon = await _host.Icons.AddReadyIcon(pack.Id, "home", [1, 2, 3]);

		var content = await _host.UiResources.TryGetAsync(PluginIconReferences.ResourceId(icon.Id), CancellationToken.None);

		Assert.That(content, Is.Null);
	}

	[Test]
	public void No_registration_can_shadow_a_plugin_icon_handle()
	{
		var store = new UiResourceStore();

		Assert.Throws<ArgumentException>(() => store.Register(new UiResourceRegistration
		{
			OwnerId = PluginIconReferences.ResourceOwnerId,
			Name = Guid.NewGuid().ToString(),
			MediaType = "image/png",
			Content = new byte[] { 1 }
		}));
	}

	private MacroDeckHost.Application.Widgets.Icons.WidgetIconSourceRegistry WidgetIconSources()
		=> new([new MacroDeckHost.Application.Widgets.Icons.IconPackWidgetIconSource(_host.Icons.Cache, _host.ScopeFactory)]);
}
