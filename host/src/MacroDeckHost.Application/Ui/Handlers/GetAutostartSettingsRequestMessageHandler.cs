using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetAutostartSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetAutostartSettingsRequest, GetAutostartSettingsResponse>
{
	private readonly IAutostartService _service;

	public GetAutostartSettingsRequestMessageHandler(IAutostartService service)
	{
		_service = service;
	}

	public ValueTask<GetAutostartSettingsResponse> Handle(
		GetAutostartSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = _service.GetSettings();

		return ValueTask.FromResult(new GetAutostartSettingsResponse
		{
			Supported = settings.Supported,
			Enabled = settings.Enabled,
			OpenMinimized = settings.OpenMinimized
		});
	}
}
