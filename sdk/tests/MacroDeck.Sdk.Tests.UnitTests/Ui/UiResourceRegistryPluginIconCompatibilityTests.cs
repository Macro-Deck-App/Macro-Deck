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

		var exception = Assert.ThrowsAsync<UiResourceException>(
			() => context.UiResources.GetPluginIconAsync("logos", "spotify"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Unsupported));
	}

	private sealed class RegistryWrittenBeforeBundledIcons : IUiResourceRegistry
	{
		public Task<UiResource> RegisterAsync(string name,
			ReadOnlyMemory<byte> content,
			string mediaType,
			CancellationToken cancellationToken = default)
			=> Task.FromResult(new UiResource { ResourceId = "test." + name });

		public Task RemoveAsync(string name, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
