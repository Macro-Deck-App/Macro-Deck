using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class RemoteUiSessionPointerMoveTests
{
	private static UiSessionEventCommand Command(string name) => new() { NodeId = "pad", Name = name };

	[TestCase(ProtocolErrorCodes.RateLimited)]
	[TestCase(ProtocolErrorCodes.Timeout)]
	public void A_busy_or_slow_plugin_drops_a_pointer_move_instead_of_failing_the_session(string code)
	{
		var provider = new RemoteUiSessionProvider("com.example.pad", new FailingInvoker(code));

		Assert.DoesNotThrowAsync(() =>
			provider.DispatchEventAsync("s1", Command(UiComponentEvents.PointerMove), CancellationToken.None));
	}

	[TestCase(UiComponentEvents.PointerUp, ProtocolErrorCodes.RateLimited)]
	[TestCase(UiComponentEvents.Press, ProtocolErrorCodes.Timeout)]
	[TestCase(UiComponentEvents.PointerMove, ProtocolErrorCodes.QueueOverflow)]
	public void Any_other_event_or_failure_still_reaches_the_broker(string name, string code)
	{
		var provider = new RemoteUiSessionProvider("com.example.pad", new FailingInvoker(code));

		Assert.ThrowsAsync<RemoteCapabilityException>(() =>
			provider.DispatchEventAsync("s1", Command(name), CancellationToken.None));
	}

	private sealed class FailingInvoker(string code) : IPluginCapabilityInvoker
	{
		public Task<JsonElement?> InvokeAsync(string pluginId,
			CapabilityInvokeRequest request,
			CancellationToken cancellationToken)
			=> throw RemoteCapabilityException.CreateRetryable(code, code);

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}
}
