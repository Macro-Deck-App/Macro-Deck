using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Companion;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

public sealed class PluginCompanionDeviceCallbacks(
	ICompanionDeviceApi devices,
	IPluginCompanionDeviceAccessPolicy accessPolicy,
	IPluginCompanionDeviceConsentNotifier? consent = null,
	IPluginSessionRegistry? sessions = null,
	IPluginHostAssetSender? assets = null)
{
	public async Task<HostCallbackResult?> AdmitAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		if (await accessPolicy.EvaluateAsync(pluginId, cancellationToken) == PluginCompanionDeviceAccess.Available)
		{
			return null;
		}

		await AskForAccessAsync(pluginId, cancellationToken);
		return HostCallbackResult.Fail(ProtocolErrorCodes.CompanionDevicesNotAllowed,
			ProtocolErrorMessages.For(ProtocolErrorCodes.CompanionDevicesNotAllowed));
	}

	public async Task<HostCallbackResult> ExecuteAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		switch (payload.Operation)
		{
			case HostOperations.CompanionDevices.Wait
				when Read<CompanionDeviceArguments>(payload) is { } arguments
				&& TryParse(arguments.DeviceId, out var id):
				return HostCallbackResult.Ok(new CompanionDeviceWaitResultDto
				{
					Ready = await devices.WaitForDeviceAsync(id, cancellationToken)
				});

			case HostOperations.CompanionDevices.Send
				when Read<CompanionDeviceCommandArguments>(payload) is { } arguments
				&& TryParse(arguments.DeviceId, out var id)
				&& CompanionDeviceWire.ToCommand(arguments) is { } command:
				return HostCallbackResult.Ok(new CompanionDeviceSendResultDto
				{
					Sent = await devices.SendAsync(id, command, cancellationToken)
				});

			case HostOperations.CompanionDevices.Request
				when Read<CompanionDeviceCommandArguments>(payload) is { } arguments
				&& TryParse(arguments.DeviceId, out var id)
				&& CompanionDeviceWire.ToCommand(arguments) is { } command:
				return HostCallbackResult.Ok(
					CompanionDeviceWire.ToDto(await devices.RequestAsync(id, command, cancellationToken)));

			case HostOperations.CompanionDevices.Screenshot
				when Read<CompanionScreenshotArguments>(payload) is { } arguments
				&& TryParse(arguments.DeviceId, out var id)
				&& CompanionDeviceWire.ToMode(arguments.Mode) is { } mode:
				return await ScreenshotAsync(pluginId, id, mode, cancellationToken);

			default:
				return HostCallbackResult.Fail(ProtocolErrorCodes.InvalidPayload,
					ProtocolErrorMessages.For(ProtocolErrorCodes.InvalidPayload));
		}
	}

	// The bytes cannot ride the reply, which is capped at the protocol message size, so they follow over
	// host.asset like an Android screenshot. The transfer is not awaited: the plugin correlates it by its id.
	private async Task<HostCallbackResult> ScreenshotAsync(
		string pluginId,
		Guid deviceId,
		CompanionScreenshotMode mode,
		CancellationToken cancellationToken)
	{
		if (assets is null)
		{
			return HostCallbackResult.Fail(ProtocolErrorCodes.CapabilityUnavailable,
				"This host cannot transfer a screenshot to a plugin.");
		}

		var result = await devices.CaptureScreenshotAsync(deviceId, mode, cancellationToken);
		if (result.Png is not { } png)
		{
			return HostCallbackResult.Ok(new CompanionScreenshotResultDto
			{
				Failure = CompanionDeviceWire.ToWire(result.Failure ?? CompanionDeviceFailure.Failed)
			});
		}

		if (png.Length > ProtocolLimits.MaxAssetBytes)
		{
			return HostCallbackResult.Ok(new CompanionScreenshotResultDto
			{
				Failure = CompanionDeviceFailures.Failed
			});
		}

		var transferId = Guid.CreateVersion7().ToString();
		var contentHash = AssetContentHash.Compute(png);
		var sender = assets;
		_ = Task.Run(() => sender.SendAsync(pluginId,
				transferId,
				AssetKinds.CompanionScreenshot,
				"image/png",
				png,
				contentHash,
				CancellationToken.None),
			CancellationToken.None);

		return HostCallbackResult.Ok(new CompanionScreenshotResultDto
		{
			ContentType = "image/png",
			ByteLength = png.Length,
			ContentHash = contentHash,
			TransferId = transferId
		});
	}

	private async Task AskForAccessAsync(string pluginId, CancellationToken cancellationToken)
	{
		if (consent is null || !accessPolicy.CanBeGranted(pluginId))
		{
			return;
		}

		var name = sessions?.Snapshot().FirstOrDefault(session => session.PluginId == pluginId)?.DisplayName
			?? pluginId;
		await consent.AskAfterRefusalAsync(pluginId, name, cancellationToken);
	}

	private static bool TryParse(string? value, out Guid deviceId)
	{
		return Guid.TryParse(value, out deviceId);
	}

	private static T? Read<T>(HostInvokePayload payload)
		where T : class
	{
		try
		{
			return payload.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}

public sealed class PluginCompanionDeviceInvokeRunner(
	IPluginCallbackRouter router,
	PluginCompanionDeviceCallbacks callbacks,
	Serilog.ILogger logger)
	: PluginBackgroundInvokeRunner(router, logger.ForContext<PluginCompanionDeviceInvokeRunner>(), "Companion device")
{
	protected override Task<HostCallbackResult?> AdmitAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		return callbacks.AdmitAsync(pluginId, payload, cancellationToken);
	}

	protected override Task<HostCallbackResult> ExecuteAsync(
		string pluginId,
		HostInvokePayload payload,
		CancellationToken cancellationToken)
	{
		return callbacks.ExecuteAsync(pluginId, payload, cancellationToken);
	}
}
