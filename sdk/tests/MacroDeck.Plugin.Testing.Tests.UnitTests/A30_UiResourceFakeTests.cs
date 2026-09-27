using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A30 - <see cref="FakeUiResourceRegistry" /> and the stub host enforce the rules Macro Deck enforces, so a
/// plugin's image code behaves the same against them as against a real host.
/// </summary>
[TestFixture]
public class A30_UiResourceFakeTests
{
	private static readonly byte[] _first = [1, 2, 3];
	private static readonly byte[] _second = [4, 5, 6, 7];
	private static readonly string[] _photoOnly = ["photo"];

	[Test]
	public async Task Registering_a_name_again_replaces_its_bytes_and_keeps_its_id()
	{
		var registry = new FakeUiResourceRegistry();

		var first = await registry.RegisterAsync("photo", _first, "image/png");
		var second = await registry.RegisterAsync("photo", _second, "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
			Assert.That(registry.Resources["photo"].Content, Is.EqualTo(_second));
			Assert.That(registry.Resources, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Exceeding_the_quota_is_refused_and_leaves_what_was_registered()
	{
		var registry = new FakeUiResourceRegistry { MaxTotalBytes = 5 };
		await registry.RegisterAsync("photo", _first, "image/png");

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.RegisterAsync("other", _second, "image/png"));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.QuotaExceeded));
			Assert.That(registry.Resources.Keys, Is.EqualTo(_photoOnly));
		});
	}

	[Test]
	public async Task Removing_frees_the_quota_and_an_unknown_name_is_not_an_error()
	{
		var registry = new FakeUiResourceRegistry { MaxCount = 1 };
		await registry.RegisterAsync("photo", _first, "image/png");

		await registry.RemoveAsync("photo");
		await registry.RemoveAsync("never-registered");

		Assert.DoesNotThrowAsync(() => registry.RegisterAsync("other", _second, "image/png"));
	}

	[TestCase("has.dot", "image/png")]
	[TestCase("photo", "image/svg+xml")]
	public void An_invalid_name_or_media_type_is_rejected_the_way_the_host_rejects_it(string name, string mediaType)
	{
		var registry = new FakeUiResourceRegistry();

		Assert.That(() => registry.RegisterAsync(name, _first, mediaType), Throws.ArgumentException);
	}

	[Test]
	public async Task Over_the_wire_the_stub_host_answers_a_registration_with_a_handle_for_the_uploaded_bytes()
	{
		var integration = new ImageIntegration();
		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin();
		builder.RegisterIntegration(_ => integration);
		await using var plugin = await host.HostAsync(builder);
		await host.WaitForSessionAsync();

		var handles = await integration.Registered.Task.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(handles.First.ContentHash, Is.EqualTo(AssetContentHash.Compute(_first)));
			Assert.That(handles.First.ByteLength, Is.EqualTo(_first.Length));
			Assert.That(handles.Again, Is.EqualTo(handles.First));
		});
	}

	[Test]
	public async Task A_fake_plugin_icon_name_matches_case_insensitively_like_the_host()
	{
		var registry = new FakeUiResourceRegistry();
		var added = registry.AddPluginIcon("logos", "spotify", _first, "image/png");

		Assert.That(await registry.GetPluginIconAsync("logos", "Spotify"), Is.EqualTo(added));
	}

	[Test]
	public async Task A_fake_plugin_icon_is_answered_by_key_and_name_outside_the_quota()
	{
		var registry = new FakeUiResourceRegistry { MaxCount = 0 };
		var added = registry.AddPluginIcon("logos", "spotify", _first, "image/png");

		var handle = await registry.GetPluginIconAsync("logos", "spotify");

		Assert.Multiple(() =>
		{
			Assert.That(handle, Is.EqualTo(added));
			Assert.That(handle.ContentHash, Is.EqualTo(AssetContentHash.Compute(_first)));
			Assert.That(handle.MediaType, Is.EqualTo("image/png"));
			Assert.That(registry.Resources, Is.Empty);
		});
	}

	[Test]
	public async Task Replacing_a_fake_plugin_icon_keeps_its_id_and_changes_its_content_hash()
	{
		var registry = new FakeUiResourceRegistry();
		var first = registry.AddPluginIcon("logos", "spotify", _first, "image/png");
		registry.AddPluginIcon("logos", "spotify", _second, "image/png");

		var replaced = await registry.GetPluginIconAsync("logos", "spotify");

		Assert.Multiple(() =>
		{
			Assert.That(replaced.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(replaced.ContentHash, Is.Not.EqualTo(first.ContentHash));
		});
	}

	[TestCase("logos", "deezer")]
	[TestCase("status", "spotify")]
	public void An_unknown_key_or_name_is_not_found(string key, string name)
	{
		var registry = new FakeUiResourceRegistry();
		registry.AddPluginIcon("logos", "spotify", _first, "image/png");

		var exception = Assert.ThrowsAsync<UiResourceException>(() => registry.GetPluginIconAsync(key, name));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.PluginIconNotFound));
	}

	[Test]
	public async Task Over_the_wire_the_stub_host_holds_no_bundled_icons()
	{
		var integration = new PluginIconIntegration();
		await using var host = await MacroDeckTestHost.StartAsync();
		var builder = MacroDeckPlugin.CreatePlugin();
		builder.RegisterIntegration(_ => integration);
		await using var plugin = await host.HostAsync(builder);
		await host.WaitForSessionAsync();

		var exception = await integration.Failure.Task.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(exception.ErrorCode, Is.EqualTo(UiResourceErrorCode.PluginIconNotFound));
	}

	private sealed class PluginIconIntegration : IPluginIntegration
	{
		public TaskCompletionSource<UiResourceException> Failure { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
		{
			try
			{
				await context.UiResources.GetPluginIconAsync("logos", "spotify");
				Failure.TrySetException(new AssertionException("The stub host answered a bundled icon."));
			}
			catch (UiResourceException exception)
			{
				Failure.TrySetResult(exception);
			}
		}

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class ImageIntegration : IPluginIntegration
	{
		public TaskCompletionSource<(UiResource First, UiResource Again)> Registered { get; } =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
		{
			try
			{
				var first = await context.UiResources.RegisterAsync("photo", _first, "image/png");
				var again = await context.UiResources.RegisterAsync("photo", _first, "image/png");
				Registered.TrySetResult((first, again));
			}
			catch (Exception exception)
			{
				Registered.TrySetException(exception);
			}
		}

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
