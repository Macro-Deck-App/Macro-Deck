using MacroDeckHost.Application.Network.Http;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class UpdateHttpSettingsRequestMessageHandler
	: IUiTransportMessageHandler<UpdateHttpSettingsRequest, UpdateHttpSettingsResponse>
{
	private readonly IHttpSettingsService _service;

	public UpdateHttpSettingsRequestMessageHandler(IHttpSettingsService service)
	{
		_service = service;
	}

	public async ValueTask<UpdateHttpSettingsResponse> Handle(
		UpdateHttpSettingsRequest request,
		CancellationToken cancellationToken)
	{
		var update = await _service.SetUserAgent(request.UserAgent);

		return new UpdateHttpSettingsResponse
		{
			Success = update.Success,
			Error = update.Success
				? null
				: new TransportError
				{
					Code = "InvalidUserAgent",
					Message = AppStrings.Errors.Http.InvalidUserAgent(HttpUserAgent.MaxLength)
				},
			CustomUserAgent = update.Settings.CustomUserAgent,
			EffectiveUserAgent = update.Settings.EffectiveUserAgent,
			DefaultUserAgent = update.Settings.DefaultUserAgent
		};
	}
}
