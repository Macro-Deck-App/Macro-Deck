using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.ScreenSaverProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.ScreenSavers;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteScreenSaverProviderContext(IHostInvoker invoker, ILogger logger)
	: IScreenSaverProviderContext
{
	private readonly ILogger _logger = logger.ForContext<RemoteScreenSaverProviderContext>();

	public async Task<ScreenSaverRegistration> RegisterScreenSaverAsync(
		ScreenSaverDescriptor screenSaver,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(screenSaver);

		JsonElement? result;
		try
		{
			result = await invoker.InvokeAsync(Protocol.Callbacks.HostApis.ScreenSavers,
				HostOperations.ScreenSavers.Register,
				new ScreenSaversRegisterArguments { ScreenSaver = ScreenSaverDescriptorMapper.ToDto(screenSaver) },
				cancellationToken);
		}
		// A host that predates screensavers answers CapabilityUnsupported for the whole api. Throwing
		// here would abort the rest of the integration's initialization, so it registers nothing instead.
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
			_logger.Information("The host does not support screensavers; '{ScreenSaverId}' is not offered",
				screenSaver.Id);

			return new ScreenSaverRegistration(string.Empty, string.Empty);
		}

		var registered = result?.Deserialize<ScreenSaversRegisterResult>(PluginProtocolJson.Options);

		return registered is null
			? new ScreenSaverRegistration(string.Empty, screenSaver.Id)
			: new ScreenSaverRegistration(registered.ScreenSaverId, registered.ProviderId);
	}

	public async Task UnregisterScreenSaverAsync(string screenSaverId, CancellationToken cancellationToken = default)
	{
		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.ScreenSavers,
				HostOperations.ScreenSavers.Unregister,
				new ScreenSaversUnregisterArguments { ScreenSaverId = screenSaverId },
				cancellationToken);
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
		}
	}
}
