using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateExtensionSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateExtensionSettingsRequest, UpdateExtensionSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public UpdateExtensionSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<UpdateExtensionSettingsResponse> Handle(
		UpdateExtensionSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetExtensions(request.StoreEnabled,
			request.CheckForUpdates,
			request.NotifyOnUpdates,
			request.RefreshIntervalMinutes);

		return new UpdateExtensionSettingsResponse
		{
			StoreEnabled = settings.StoreEnabled,
			CheckForUpdates = settings.CheckForUpdates,
			NotifyOnUpdates = settings.NotifyOnUpdates,
			RefreshIntervalMinutes = settings.RefreshIntervalMinutes
		};
	}
}
