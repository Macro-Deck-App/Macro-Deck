using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/scripts")]
public class ScriptsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetScriptsRequest, GetScriptsResponse> _getScripts;
	private readonly IUiTransportMessageHandler<GetScriptUsagesRequest, GetScriptUsagesResponse> _getScriptUsages;
	private readonly IUiTransportMessageHandler<CreateScriptRequest, CreateScriptResponse> _createScript;
	private readonly IUiTransportMessageHandler<UpdateScriptRequest, UpdateScriptResponse> _updateScript;
	private readonly IUiTransportMessageHandler<DuplicateScriptRequest, DuplicateScriptResponse> _duplicateScript;
	private readonly IUiTransportMessageHandler<DeleteScriptRequest, DeleteScriptResponse> _deleteScript;
	private readonly IUiTransportMessageHandler<RunScriptRequest, RunScriptResponse> _runScript;

	public ScriptsController(
		IUiTransportMessageHandler<GetScriptsRequest, GetScriptsResponse> getScripts,
		IUiTransportMessageHandler<GetScriptUsagesRequest, GetScriptUsagesResponse> getScriptUsages,
		IUiTransportMessageHandler<CreateScriptRequest, CreateScriptResponse> createScript,
		IUiTransportMessageHandler<UpdateScriptRequest, UpdateScriptResponse> updateScript,
		IUiTransportMessageHandler<DuplicateScriptRequest, DuplicateScriptResponse> duplicateScript,
		IUiTransportMessageHandler<DeleteScriptRequest, DeleteScriptResponse> deleteScript,
		IUiTransportMessageHandler<RunScriptRequest, RunScriptResponse> runScript)
	{
		_getScripts = getScripts;
		_getScriptUsages = getScriptUsages;
		_createScript = createScript;
		_updateScript = updateScript;
		_duplicateScript = duplicateScript;
		_deleteScript = deleteScript;
		_runScript = runScript;
	}

	[HttpGet]
	public Task<GetScriptsResponse> GetAll(CancellationToken ct)
		=> _getScripts.Handle(new GetScriptsRequest(), ct).AsTask();

	[HttpGet("{id}/usages")]
	public Task<GetScriptUsagesResponse> GetUsages(string id, CancellationToken ct)
		=> _getScriptUsages.Handle(new GetScriptUsagesRequest { Id = id }, ct).AsTask();

	[HttpPost]
	public Task<CreateScriptResponse> Create(CreateScriptRequest body, CancellationToken ct)
		=> _createScript.Handle(body, ct).AsTask();

	[HttpPut("{id}")]
	public Task<UpdateScriptResponse> Update(string id, UpdateScriptRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _updateScript.Handle(body, ct).AsTask();
	}

	[HttpPost("{id}/duplicate")]
	public Task<DuplicateScriptResponse> Duplicate(string id, CancellationToken ct)
		=> _duplicateScript.Handle(new DuplicateScriptRequest { Id = id }, ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DeleteScriptResponse> Delete(string id, CancellationToken ct)
		=> _deleteScript.Handle(new DeleteScriptRequest { Id = id }, ct).AsTask();

	[HttpPost("{id}/run")]
	public Task<RunScriptResponse> Run(string id, RunScriptRequest body, CancellationToken ct)
	{
		body.Id = id;
		return _runScript.Handle(body, ct).AsTask();
	}
}
