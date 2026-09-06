using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateLoggingSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateLoggingSettingsRequest, UpdateLoggingSettingsResponse>
{
	private readonly IAppPreferenceService _service;
	private readonly ILogLevelState _logLevelState;

	public UpdateLoggingSettingsRequestMessageHandler(IAppPreferenceService service, ILogLevelState logLevelState)
	{
		_service = service;
		_logLevelState = logLevelState;
	}

	public async ValueTask<UpdateLoggingSettingsResponse> Handle(
		UpdateLoggingSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.SetLogging(request.MinimumLevel);

		_logLevelState.Minimum = settings.MinimumLevel;

		return new UpdateLoggingSettingsResponse
		{
			MinimumLevel = settings.MinimumLevel,
			DefaultMinimumLevel = settings.DefaultMinimumLevel
		};
	}
}
