using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetExtensionSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetExtensionSettingsRequest, GetExtensionSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public GetExtensionSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetExtensionSettingsResponse> Handle(
		GetExtensionSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetExtensions();

		return new GetExtensionSettingsResponse
		{
			StoreEnabled = settings.StoreEnabled,
			CheckForUpdates = settings.CheckForUpdates,
			NotifyOnUpdates = settings.NotifyOnUpdates,
			RefreshIntervalMinutes = settings.RefreshIntervalMinutes
		};
	}
}
