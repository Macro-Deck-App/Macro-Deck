using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/integrations")]
public class IntegrationsController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetIntegrationsRequest, GetIntegrationsResponse> _getIntegrations;

	private readonly IUiTransportMessageHandler<SetIntegrationEnabledRequest, SetIntegrationEnabledResponse>
		_setIntegrationEnabled;

	private readonly IUiTransportMessageHandler<StartConfigFlowRequest, StartConfigFlowResponse> _startConfigFlow;

	private readonly IUiTransportMessageHandler<SubmitConfigFlowStepRequest, SubmitConfigFlowStepResponse>
		_submitConfigFlowStep;

	private readonly IUiTransportMessageHandler<GetConfigEntriesRequest, GetConfigEntriesResponse> _getConfigEntries;
	private readonly IUiTransportMessageHandler<DeleteConfigEntryRequest, DeleteConfigEntryResponse> _deleteConfigEntry;
	private readonly IUiTransportMessageHandler<RenameConfigEntryRequest, RenameConfigEntryResponse> _renameConfigEntry;
	private readonly IUiTransportMessageHandler<GetIntegrationIssuesRequest, GetIntegrationIssuesResponse> _getIssues;

	private readonly IUiTransportMessageHandler<ResolveIntegrationIssueRequest, ResolveIntegrationIssueResponse>
		_resolveIssue;

	private readonly IUiTransportMessageHandler<GetIntegrationCapabilitiesRequest, GetIntegrationCapabilitiesResponse>
		_getCapabilities;

	public IntegrationsController(
		IUiTransportMessageHandler<GetIntegrationsRequest, GetIntegrationsResponse> getIntegrations,
		IUiTransportMessageHandler<SetIntegrationEnabledRequest, SetIntegrationEnabledResponse> setIntegrationEnabled,
		IUiTransportMessageHandler<StartConfigFlowRequest, StartConfigFlowResponse> startConfigFlow,
		IUiTransportMessageHandler<SubmitConfigFlowStepRequest, SubmitConfigFlowStepResponse> submitConfigFlowStep,
		IUiTransportMessageHandler<GetConfigEntriesRequest, GetConfigEntriesResponse> getConfigEntries,
		IUiTransportMessageHandler<DeleteConfigEntryRequest, DeleteConfigEntryResponse> deleteConfigEntry,
		IUiTransportMessageHandler<RenameConfigEntryRequest, RenameConfigEntryResponse> renameConfigEntry,
		IUiTransportMessageHandler<GetIntegrationIssuesRequest, GetIntegrationIssuesResponse> getIssues,
		IUiTransportMessageHandler<ResolveIntegrationIssueRequest, ResolveIntegrationIssueResponse> resolveIssue,
		IUiTransportMessageHandler<GetIntegrationCapabilitiesRequest, GetIntegrationCapabilitiesResponse>
			getCapabilities)
	{
		_getIntegrations = getIntegrations;
		_setIntegrationEnabled = setIntegrationEnabled;
		_startConfigFlow = startConfigFlow;
		_submitConfigFlowStep = submitConfigFlowStep;
		_getConfigEntries = getConfigEntries;
		_deleteConfigEntry = deleteConfigEntry;
		_renameConfigEntry = renameConfigEntry;
		_getIssues = getIssues;
		_resolveIssue = resolveIssue;
		_getCapabilities = getCapabilities;
	}

	[HttpGet]
	public Task<GetIntegrationsResponse> GetAll(CancellationToken ct)
		=> _getIntegrations.Handle(new GetIntegrationsRequest(), ct).AsTask();

	[HttpPatch("{id}/enabled")]
	public Task<SetIntegrationEnabledResponse> SetEnabled(string id,
		SetIntegrationEnabledRequest body,
		CancellationToken ct)
	{
		body.Id = id;
		return _setIntegrationEnabled.Handle(body, ct).AsTask();
	}

	[HttpPost("{id}/config-flow/start")]
	public Task<StartConfigFlowResponse> StartConfigFlow(string id, CancellationToken ct)
		=> _startConfigFlow.Handle(new StartConfigFlowRequest { IntegrationId = id }, ct).AsTask();

	[HttpPost("{id}/config-flow/start-new")]
	public Task<StartConfigFlowResponse> StartNewConfigFlow(
		string id,
		StartConfigFlowRequest body,
		CancellationToken ct)
	{
		body.IntegrationId = id;
		body.EntryId = null;
		body.Title ??= string.Empty;
		return _startConfigFlow.Handle(body, ct).AsTask();
	}

	[HttpPost("{id}/config-entries/{entryId}/config-flow/start")]
	public Task<StartConfigFlowResponse> StartEditConfigFlow(string id, string entryId, CancellationToken ct)
		=> _startConfigFlow.Handle(new StartConfigFlowRequest { IntegrationId = id, EntryId = entryId }, ct)
			.AsTask();

	[HttpPost("{id}/config-flow/submit")]
	public Task<SubmitConfigFlowStepResponse> SubmitConfigFlowStep(string id,
		SubmitConfigFlowStepRequest body,
		CancellationToken ct)
		=> _submitConfigFlowStep.Handle(body, ct).AsTask();

	[HttpGet("{id}/config-entries")]
	public Task<GetConfigEntriesResponse> GetConfigEntries(string id, CancellationToken ct)
		=> _getConfigEntries.Handle(new GetConfigEntriesRequest { IntegrationId = id }, ct).AsTask();

	[HttpDelete("{id}/config-entries/{entryId}")]
	public Task<DeleteConfigEntryResponse> DeleteConfigEntry(
		string id,
		string entryId,
		[FromQuery] bool confirmed,
		CancellationToken ct)
		=> _deleteConfigEntry.Handle(new DeleteConfigEntryRequest
				{
					IntegrationId = id,
					EntryId = entryId,
					Confirmed = confirmed
				},
				ct)
			.AsTask();

	[HttpPatch("{id}/config-entries/{entryId}")]
	public Task<RenameConfigEntryResponse> RenameConfigEntry(
		string id,
		string entryId,
		RenameConfigEntryRequest body,
		CancellationToken ct)
	{
		body.IntegrationId = id;
		body.EntryId = entryId;
		return _renameConfigEntry.Handle(body, ct).AsTask();
	}

	[HttpGet("{id}/issues")]
	public Task<GetIntegrationIssuesResponse> GetIssues(string id, CancellationToken ct)
		=> _getIssues.Handle(new GetIntegrationIssuesRequest { IntegrationId = id }, ct).AsTask();

	[HttpPost("{id}/issues/{issueId}/resolve")]
	public Task<ResolveIntegrationIssueResponse> ResolveIssue(string id, string issueId, CancellationToken ct)
		=> _resolveIssue.Handle(new ResolveIntegrationIssueRequest { IntegrationId = id, IssueId = issueId }, ct)
			.AsTask();

	[HttpGet("{id}/capabilities")]
	public Task<GetIntegrationCapabilitiesResponse> GetCapabilities(string id, CancellationToken ct)
		=> _getCapabilities.Handle(new GetIntegrationCapabilitiesRequest { IntegrationId = id }, ct).AsTask();
}
