using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

/// <summary>
/// Proxies <see cref="IActionInteractions"/> over <c>host.invoke</c> against
/// <see cref="HostApis.ActionInteractions"/>, one per <c>actions/execute</c> invocation - see
/// <c>ActionsCapabilityHandler.ExecuteAsync</c>, which builds one from the ambient
/// <c>ICapabilityInvocationContext.CorrelationId</c> so the host can verify a picker request belongs
/// to a live execution of this plugin before honouring it. Both members are fire-and-forget by
/// contract, same posture as <see cref="RemoteUserNotifier"/>.
/// </summary>
internal sealed class RemoteActionInteractions(IHostInvoker invoker, ILogger logger, string executeCorrelationId)
	: IActionInteractions
{
	public void RequestItemPicker(string? originClientId,
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? prompt = null)
		=> Fire(HostOperations.ActionInteractions.RequestItemPicker,
			new ActionInteractionsRequestItemPickerArguments
			{
				ExecuteCorrelationId = executeCorrelationId,
				OriginClientId = originClientId,
				InstanceId = instanceId,
				Kind = kind.ToString(),
				Prompt = prompt
			},
			"request-item-picker");

	public void RequestDevicePicker(string? originClientId,
		string instanceId,
		bool startPlayback,
		string? prompt = null)
		=> Fire(HostOperations.ActionInteractions.RequestDevicePicker,
			new ActionInteractionsRequestDevicePickerArguments
			{
				ExecuteCorrelationId = executeCorrelationId,
				OriginClientId = originClientId,
				InstanceId = instanceId,
				StartPlayback = startPlayback,
				Prompt = prompt
			},
			"request-device-picker");

	private void Fire(string operation, object arguments, string label)
	{
		_ = InvokeBestEffortAsync(operation, arguments, label);
	}

	private async Task InvokeBestEffortAsync(string operation, object arguments, string label)
	{
		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.ActionInteractions,
					operation,
					arguments,
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			logger.HostCallbackFailed(Protocol.Callbacks.HostApis.ActionInteractions, label, exception);
		}
	}
}
