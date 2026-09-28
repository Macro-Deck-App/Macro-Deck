using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/shared-variables")]
public class SharedVariablesController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetSharedVariablesRequest, GetSharedVariablesResponse> _list;

	private readonly IUiTransportMessageHandler<SetSharedVariableValueRequest, SetSharedVariableValueResponse>
		_setValue;

	public SharedVariablesController(
		IUiTransportMessageHandler<GetSharedVariablesRequest, GetSharedVariablesResponse> list,
		IUiTransportMessageHandler<SetSharedVariableValueRequest, SetSharedVariableValueResponse> setValue)
	{
		_list = list;
		_setValue = setValue;
	}

	[HttpGet]
	public Task<GetSharedVariablesResponse> List(CancellationToken ct)
		=> _list.Handle(new GetSharedVariablesRequest(), ct).AsTask();

	[HttpPut("{name}/value")]
	public Task<SetSharedVariableValueResponse> SetValue(string name,
		SetSharedVariableValueRequest body,
		CancellationToken ct)
	{
		body.Name = name;
		return _setValue.Handle(body, ct).AsTask();
	}
}
