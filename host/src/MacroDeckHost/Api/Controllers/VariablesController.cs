using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/variables")]
public class VariablesController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetVariablesRequest, GetVariablesResponse> _getVariables;
	private readonly IUiTransportMessageHandler<CreateVariableRequest, CreateVariableResponse> _createVariable;
	private readonly IUiTransportMessageHandler<UpdateVariableRequest, UpdateVariableResponse> _updateVariable;
	private readonly IUiTransportMessageHandler<DeleteVariableRequest, DeleteVariableResponse> _deleteVariable;
	private readonly IUiTransportMessageHandler<SetVariableValueRequest, SetVariableValueResponse> _setVariableValue;

	private readonly IUiTransportMessageHandler<SanitizeVariableNameRequest, SanitizeVariableNameResponse>
		_sanitizeVariableName;

	private readonly IUiTransportMessageHandler<BindCatalogVariableRequest, BindCatalogVariableResponse>
		_bindCatalogVariable;

	private readonly IUiTransportMessageHandler<UnbindCatalogVariableRequest, UnbindCatalogVariableResponse>
		_unbindCatalogVariable;

	private readonly IUiTransportMessageHandler<RenameCatalogVariableRequest, RenameCatalogVariableResponse>
		_renameCatalogVariable;

	public VariablesController(
		IUiTransportMessageHandler<GetVariablesRequest, GetVariablesResponse> getVariables,
		IUiTransportMessageHandler<CreateVariableRequest, CreateVariableResponse> createVariable,
		IUiTransportMessageHandler<UpdateVariableRequest, UpdateVariableResponse> updateVariable,
		IUiTransportMessageHandler<DeleteVariableRequest, DeleteVariableResponse> deleteVariable,
		IUiTransportMessageHandler<SetVariableValueRequest, SetVariableValueResponse> setVariableValue,
		IUiTransportMessageHandler<SanitizeVariableNameRequest, SanitizeVariableNameResponse> sanitizeVariableName,
		IUiTransportMessageHandler<BindCatalogVariableRequest, BindCatalogVariableResponse> bindCatalogVariable,
		IUiTransportMessageHandler<UnbindCatalogVariableRequest, UnbindCatalogVariableResponse> unbindCatalogVariable,
		IUiTransportMessageHandler<RenameCatalogVariableRequest, RenameCatalogVariableResponse> renameCatalogVariable)
	{
		_getVariables = getVariables;
		_createVariable = createVariable;
		_updateVariable = updateVariable;
		_deleteVariable = deleteVariable;
		_setVariableValue = setVariableValue;
		_sanitizeVariableName = sanitizeVariableName;
		_bindCatalogVariable = bindCatalogVariable;
		_unbindCatalogVariable = unbindCatalogVariable;
		_renameCatalogVariable = renameCatalogVariable;
	}

	[HttpGet]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetVariablesResponse> GetAll(CancellationToken ct)
		=> _getVariables.Handle(new GetVariablesRequest(), ct).AsTask();

	[HttpPost]
	public Task<CreateVariableResponse> Create(CreateVariableRequest body, CancellationToken ct)
		=> _createVariable.Handle(body, ct).AsTask();

	[HttpPut]
	public Task<UpdateVariableResponse> Update(UpdateVariableRequest body, CancellationToken ct)
		=> _updateVariable.Handle(body, ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DeleteVariableResponse> Delete(string id, CancellationToken ct)
		=> _deleteVariable.Handle(new DeleteVariableRequest { Id = id }, ct).AsTask();

	[HttpPatch("{id}/value")]
	public Task<SetVariableValueResponse> SetValue(string id, SetVariableValueRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _setVariableValue.Handle(body, ct).AsTask();
	}

	[HttpPost("sanitize-name")]
	public Task<SanitizeVariableNameResponse> SanitizeName(SanitizeVariableNameRequest body, CancellationToken ct)
		=> _sanitizeVariableName.Handle(body, ct).AsTask();

	[HttpPost("catalog/bind")]
	public Task<BindCatalogVariableResponse> BindCatalog(BindCatalogVariableRequest body, CancellationToken ct)
		=> _bindCatalogVariable.Handle(body, ct).AsTask();

	[HttpDelete("catalog/{id}")]
	public Task<UnbindCatalogVariableResponse> UnbindCatalog(string id, CancellationToken ct)
		=> _unbindCatalogVariable.Handle(new UnbindCatalogVariableRequest { VariableId = id }, ct).AsTask();

	[HttpPatch("catalog/{id}/name")]
	public Task<RenameCatalogVariableResponse> RenameCatalog(string id,
		RenameCatalogVariableRequest body,
		CancellationToken ct)
	{
		body.VariableId = id;
		return _renameCatalogVariable.Handle(body, ct).AsTask();
	}
}
