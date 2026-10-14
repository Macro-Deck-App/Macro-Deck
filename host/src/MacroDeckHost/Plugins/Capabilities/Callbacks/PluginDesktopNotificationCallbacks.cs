using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Notifications;
using MacroDeckHost.Application.Plugins;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginDesktopNotificationCallbacks(
	IDesktopNotifier notifier,
	IPluginDesktopNotificationAccessPolicy accessPolicy)
{
	public bool IsSupportedFor(string pluginId)
	{
		return accessPolicy.IsAllowed(pluginId) && notifier.IsSupported;
	}

	public HostCallbackResult? Admit(string pluginId)
	{
		return accessPolicy.IsAllowed(pluginId)
			? null
			: HostCallbackResult.Fail(ProtocolErrorCodes.DesktopNotificationsNotAllowed,
				ProtocolErrorMessages.For(ProtocolErrorCodes.DesktopNotificationsNotAllowed));
	}

	public async Task<HostCallbackResult> ExecuteAsync(HostInvokePayload payload, CancellationToken cancellationToken)
	{
		if (payload.Operation != HostOperations.DesktopNotifications.Show
			|| Read(payload) is not { Title: not null, Message: not null } arguments)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
				ProtocolErrorMessages.For(ProtocolErrorCodes.InvalidPayload));
		}

		await notifier.ShowAsync(arguments.Title, arguments.Message, cancellationToken);
		return HostCallbackResult.Ok();
	}

	private static DesktopNotificationShowArguments? Read(HostInvokePayload payload)
	{
		try
		{
			return payload.Arguments?.Deserialize<DesktopNotificationShowArguments>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}

public sealed class PluginDesktopNotificationInvokeRunner(
	IPluginCallbackRouter router,
	PluginDesktopNotificationCallbacks callbacks,
	Serilog.ILogger logger)
	: PluginBackgroundInvokeRunner(router,
		logger.ForContext<PluginDesktopNotificationInvokeRunner>(),
		"desktop notification")
{
	protected override Task<HostCallbackResult?> AdmitAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(callbacks.Admit(pluginId));
	}

	protected override Task<HostCallbackResult> ExecuteAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		return callbacks.ExecuteAsync(payload, cancellationToken);
	}
}
