using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Localization;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/localization")]
public class LocalizationController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetLocalizationRequest, GetLocalizationResponse> _get;

	public LocalizationController(IUiTransportMessageHandler<GetLocalizationRequest, GetLocalizationResponse> get)
	{
		_get = get;
	}

	[HttpGet]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetLocalizationResponse> Get(CancellationToken ct)
		=> _get.Handle(new GetLocalizationRequest(), ct).AsTask();
}
