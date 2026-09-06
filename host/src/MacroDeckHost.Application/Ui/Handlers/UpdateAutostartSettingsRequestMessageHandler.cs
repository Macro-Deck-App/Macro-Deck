using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateAutostartSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateAutostartSettingsRequest, UpdateAutostartSettingsResponse>
{
	private readonly IAutostartService _service;

	public UpdateAutostartSettingsRequestMessageHandler(IAutostartService service)
	{
		_service = service;
	}

	public ValueTask<UpdateAutostartSettingsResponse> Handle(
		UpdateAutostartSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var result = _service.Update(request.Enabled, request.OpenMinimized);
		var settings = result.Success ? result.Data! : _service.GetSettings();

		return ValueTask.FromResult(new UpdateAutostartSettingsResponse
		{
			Success = result.Success,
			Error = result.Success ? null : result.ErrorMessage,
			Supported = settings.Supported,
			Enabled = settings.Enabled,
			OpenMinimized = settings.OpenMinimized
		});
	}
}
