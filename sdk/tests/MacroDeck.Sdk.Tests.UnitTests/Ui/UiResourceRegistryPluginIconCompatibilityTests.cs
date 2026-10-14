using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Sdk.Tests.UnitTests.Ui;

[TestFixture]
public class UiResourceRegistryPluginIconCompatibilityTests
{
	[Test]
	public void A_registry_written_before_bundled_icons_existed_still_builds_and_reports_them_unsupported()
	{
		IUiResourceRegistry registry = new RegistryWrittenBeforeBundledIcons();

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.GetPluginIconAsync("logos", "spotify"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public async Task A_registry_written_before_bundled_icons_existed_keeps_registering_as_before()
	{
		IUiResourceRegistry registry = new RegistryWrittenBeforeBundledIcons();

		var handle = await registry.RegisterAsync("photo", new byte[] { 1 }, "image/png");

		Assert.That(handle.ResourceId, Is.EqualTo("test.photo"));
	}

	[Test]
	public void A_context_written_before_ui_resources_existed_reports_bundled_icons_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeUiResources();

		var exception
			= Assert.ThrowsAsync<UiResourceException>(() => context.UiResources.GetPluginIconAsync("logos", "spotify"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public void A_registry_written_before_icon_lookup_by_id_existed_still_builds_and_reports_it_unsupported()
	{
		IUiResourceRegistry registry = new RegistryWrittenBeforeBundledIcons();

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.GetIconAsync(Guid.NewGuid()));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public void A_context_written_before_ui_resources_existed_reports_icon_lookup_by_id_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeUiResources();

		var exception = Assert.ThrowsAsync<UiResourceException>(() => context.UiResources.GetIconAsync(Guid.NewGuid()));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public void A_registry_written_before_music_player_artwork_existed_still_builds_and_reports_it_unsupported()
	{
		IUiResourceRegistry registry = new RegistryWrittenBeforeBundledIcons();

		var exception = Assert.ThrowsAsync<UiResourceException>(()
			=> registry.RegisterMusicPlayerArtworkAsync("cover", "net.example.jukebox::default", "cover-1"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public void A_context_written_before_ui_resources_existed_reports_music_player_artwork_unsupported()
	{
		IIntegrationContext context = new ContextWrittenBeforeUiResources();

		var exception = Assert.ThrowsAsync<UiResourceException>(()
			=> context.UiResources.RegisterMusicPlayerArtworkAsync("cover", "net.example.jukebox::default", "cover-1"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	[Test]
	public async Task
		A_registry_written_before_the_artwork_size_existed_registers_the_full_size_cover_for_a_sized_request()
	{
		var written = new RegistryWrittenBeforeTheArtworkSize();
		IUiResourceRegistry registry = written;

		var handle = await registry.RegisterMusicPlayerArtworkAsync("cover",
			"net.example.jukebox::default",
			"cover-1",
			256);

		Assert.Multiple(() =>
		{
			Assert.That(handle!.ResourceId, Is.EqualTo("test.cover"));
			Assert.That(written.Requests, Is.EqualTo(new[] { ("cover", "net.example.jukebox::default", "cover-1") }));
		});
	}

	[Test]
	public void A_sized_artwork_request_with_a_size_that_is_not_positive_is_refused()
	{
		IUiResourceRegistry registry = new RegistryWrittenBeforeTheArtworkSize();

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_ = registry.RegisterMusicPlayerArtworkAsync("cover", "net.example.jukebox::default", "cover-1", 0));
	}

	private sealed class RegistryWrittenBeforeTheArtworkSize : IUiResourceRegistry
	{
		public List<(string Name, string InstanceId, string ArtworkId)> Requests { get; } = [];

		public Task<UiResource> RegisterAsync(
			string name,
			ReadOnlyMemory<byte> content,
			string mediaType,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new UiResource { ResourceId = "test." + name });
		}

		public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public Task<UiResource?> RegisterMusicPlayerArtworkAsync(
			string name,
			string instanceId,
			string artworkId,
			CancellationToken cancellationToken = default)
		{
			Requests.Add((name, instanceId, artworkId));
			return Task.FromResult<UiResource?>(new UiResource { ResourceId = "test." + name });
		}
	}

	private sealed class RegistryWrittenBeforeBundledIcons : IUiResourceRegistry
	{
		public Task<UiResource> RegisterAsync(
			string name,
			ReadOnlyMemory<byte> content,
			string mediaType,
			CancellationToken cancellationToken = default)
		{
			return Task.FromResult(new UiResource { ResourceId = "test." + name });
		}

		public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class ContextWrittenBeforeUiResources : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
