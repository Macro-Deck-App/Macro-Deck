using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetHttpSettingsRequestMessageHandler
	: IUiTransportMessageHandler<GetHttpSettingsRequest, GetHttpSettingsResponse>
{
	private readonly IHttpSettingsService _service;

	public GetHttpSettingsRequestMessageHandler(IHttpSettingsService service)
	{
		_service = service;
	}

	public async ValueTask<GetHttpSettingsResponse> Handle(
		GetHttpSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var settings = await _service.Load();

		return new GetHttpSettingsResponse
		{
			CustomUserAgent = settings.CustomUserAgent,
			EffectiveUserAgent = settings.EffectiveUserAgent,
			DefaultUserAgent = settings.DefaultUserAgent
		};
	}
}
