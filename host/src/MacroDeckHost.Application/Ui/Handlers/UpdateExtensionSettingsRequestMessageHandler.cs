using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateExtensionSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateExtensionSettingsRequest, UpdateExtensionSettingsResponse>
{
	private readonly IAppPreferenceService _service;
	private readonly IStoreUpdateDetector _updateDetector;

	public UpdateExtensionSettingsRequestMessageHandler(IAppPreferenceService service,
		IStoreUpdateDetector updateDetector)
	{
		_service = service;
		_updateDetector = updateDetector;
	}

	public async ValueTask<UpdateExtensionSettingsResponse> Handle(
		UpdateExtensionSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetExtensions(request.StoreEnabled,
			request.CheckForUpdates,
			request.NotifyOnUpdates,
			request.RefreshIntervalMinutes,
			request.AutoUpdate);
		_updateDetector.Check();

		return new UpdateExtensionSettingsResponse
		{
			StoreEnabled = settings.StoreEnabled,
			CheckForUpdates = settings.CheckForUpdates,
			NotifyOnUpdates = settings.NotifyOnUpdates,
			RefreshIntervalMinutes = settings.RefreshIntervalMinutes,
			AutoUpdate = settings.AutoUpdate
		};
	}
}
