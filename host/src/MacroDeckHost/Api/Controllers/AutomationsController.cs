using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/automations")]
public class AutomationsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetAutomationsRequest, GetAutomationsResponse> _getAutomations;
	private readonly IUiTransportMessageHandler<CreateAutomationRequest, CreateAutomationResponse> _createAutomation;
	private readonly IUiTransportMessageHandler<UpdateAutomationRequest, UpdateAutomationResponse> _updateAutomation;

	private readonly IUiTransportMessageHandler<DuplicateAutomationRequest, DuplicateAutomationResponse>
		_duplicateAutomation;

	private readonly IUiTransportMessageHandler<DeleteAutomationRequest, DeleteAutomationResponse> _deleteAutomation;

	public AutomationsController(
		IUiTransportMessageHandler<GetAutomationsRequest, GetAutomationsResponse> getAutomations,
		IUiTransportMessageHandler<CreateAutomationRequest, CreateAutomationResponse> createAutomation,
		IUiTransportMessageHandler<UpdateAutomationRequest, UpdateAutomationResponse> updateAutomation,
		IUiTransportMessageHandler<DuplicateAutomationRequest, DuplicateAutomationResponse> duplicateAutomation,
		IUiTransportMessageHandler<DeleteAutomationRequest, DeleteAutomationResponse> deleteAutomation)
	{
		_getAutomations = getAutomations;
		_createAutomation = createAutomation;
		_updateAutomation = updateAutomation;
		_duplicateAutomation = duplicateAutomation;
		_deleteAutomation = deleteAutomation;
	}

	[HttpGet]
	public Task<GetAutomationsResponse> GetAll(CancellationToken ct)
		=> _getAutomations.Handle(new GetAutomationsRequest(), ct).AsTask();

	[HttpPost]
	public Task<CreateAutomationResponse> Create(CreateAutomationRequest body, CancellationToken ct)
		=> _createAutomation.Handle(body, ct).AsTask();

	[HttpPut("{id}")]
	public Task<UpdateAutomationResponse> Update(string id, UpdateAutomationRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _updateAutomation.Handle(body, ct).AsTask();
	}

	[HttpPost("{id}/duplicate")]
	public Task<DuplicateAutomationResponse> Duplicate(string id, CancellationToken ct)
		=> _duplicateAutomation.Handle(new DuplicateAutomationRequest { Id = id }, ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DeleteAutomationResponse> Delete(string id, CancellationToken ct)
		=> _deleteAutomation.Handle(new DeleteAutomationRequest { Id = id }, ct).AsTask();
}
