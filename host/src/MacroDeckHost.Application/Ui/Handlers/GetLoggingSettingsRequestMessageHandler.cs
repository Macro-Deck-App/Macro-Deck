using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetLoggingSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetLoggingSettingsRequest, GetLoggingSettingsResponse>
{
	private readonly IAppPreferenceService _service;

	public GetLoggingSettingsRequestMessageHandler(IAppPreferenceService service)
	{
		_service = service;
	}

	public async ValueTask<GetLoggingSettingsResponse> Handle(
		GetLoggingSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.GetLogging();

		return new GetLoggingSettingsResponse
		{
			MinimumLevel = settings.MinimumLevel,
			DefaultMinimumLevel = settings.DefaultMinimumLevel
		};
	}
}
