using System.ComponentModel;
using System.Text.Json;
using MacroDeckHost.Application.Mcp;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp.Tools;

public sealed record McpRunResult(string Status, string? ErrorCode, string? Message, long DurationMs);

public sealed record McpTriggerResult(bool Triggered, int QueuedTriggers);

[McpServerToolType]
public sealed class McpRunTools
{
	public const string ProviderBlocked = "PROVIDER_BLOCKED";
	public const string ForeignSecretRef = "FOREIGN_SECRET_REF";

	private readonly IUiTransportMessageHandler<ExecuteActionRequest, ExecuteActionResponse> _executeAction;
	private readonly IUiTransportMessageHandler<RunScriptRequest, RunScriptResponse> _runScript;
	private readonly IUiTransportMessageHandler<TriggerEventRequest, TriggerEventResponse> _triggerEvent;
	private readonly IMcpRiskPolicy _risk;
	private readonly IMcpRunInspector _inspector;
	private readonly IMcpProtectedReferences _references;
	private readonly IMcpSecretLedger _secrets;
	private readonly SecretWriteAuthority _authority;
	private readonly McpTextResolver _text;
	private readonly McpConfirmationText _confirmation;
	private readonly McpConfirmationGate _gate;

	public McpRunTools(
		IUiTransportMessageHandler<ExecuteActionRequest, ExecuteActionResponse> executeAction,
		IUiTransportMessageHandler<RunScriptRequest, RunScriptResponse> runScript,
		IUiTransportMessageHandler<TriggerEventRequest, TriggerEventResponse> triggerEvent,
		IMcpRiskPolicy risk,
		IMcpRunInspector inspector,
		IMcpProtectedReferences references,
		IMcpSecretLedger secrets,
		SecretWriteAuthority authority,
		McpTextResolver text,
		McpConfirmationText confirmation,
		McpConfirmationGate gate)
	{
		_confirmation = confirmation;
		_gate = gate;
		_executeAction = executeAction;
		_runScript = runScript;
		_triggerEvent = triggerEvent;
		_risk = risk;
		_inspector = inspector;
		_references = references;
		_secrets = secrets;
		_authority = authority;
		_text = text;
	}

	[McpServerTool(Name = "run_action",
		Title = "Run action",
		Destructive = true,
		OpenWorld = true,
		UseStructuredContent = true)]
	[Description("Runs one action from the catalog with the given parameters. Actions the user marked for "
		+ "confirmation ask the user first; actions turned off for AI clients are refused. A secret parameter "
		+ "takes only a reference this client created with set_secret, and it is passed on unresolved.")]
	public async Task<McpRunResult> RunAction(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		[Description("The integrationId from the catalog.")]
		string integrationId,
		[Description("The actionId from the catalog.")]
		string actionId,
		[Description("Parameter values by name.")]
		Dictionary<string, JsonElement>? parameters = null,
		CancellationToken cancellationToken = default)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		var tokenId = McpConsent.TokenId(context);
		RefuseForeignSecrets(tokenId, JsonSerializer.Serialize(parameters));

		var details = new List<(string Code, string Text)>();
		await AddRisk(integrationId, actionId, details, parameters);
		var protectedNames = _references.ProtectedVariables();
		var steered = (parameters ?? [])
			.Values
			.Where(value => value.ValueKind == JsonValueKind.String)
			.Select(value => value.GetString()!)
			.Where(protectedNames.Contains)
			.Distinct(StringComparer.Ordinal)
			.ToList();
		foreach (var name in steered)
		{
			details.Add(($"{McpConfirmationKinds.ProtectedVariable}: {name}",
				await _confirmation.ProtectedVariable(name)));
		}

		await Confirm(server, context, details, cancellationToken);

		var response = await _executeAction.Handle(new ExecuteActionRequest
			{
				IntegrationId = integrationId,
				ActionId = actionId,
				Parameters = parameters,
				McpTokenId = tokenId,
				McpConsentedVariables = steered
			},
			cancellationToken);

		return new McpRunResult(response.Status.ToString(),
			response.Error?.Code,
			await _text.Resolve(response.Error?.Message ?? default),
			response.DurationMs);
	}

	[McpServerTool(Name = "run_script",
		Title = "Run script",
		Destructive = true,
		OpenWorld = true,
		UseStructuredContent = true)]
	[Description("Runs a script with the given inputs. Every action the script would run, including through "
		+ "nested scripts, is checked like run_action.")]
	public async Task<McpRunResult> RunScript(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		[Description("The script id from list.")]
		string scriptId,
		[Description("Input values by name.")] Dictionary<string, JsonElement>? inputs = null,
		CancellationToken cancellationToken = default)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		var tokenId = McpConsent.TokenId(context);
		if (!Guid.TryParse(scriptId, out var id))
		{
			throw McpToolErrors.Create(McpToolErrors.InvalidArgument, "scriptId must be an id from list.");
		}

		RefuseForeignSecrets(tokenId, JsonSerializer.Serialize(inputs));
		var details = new List<(string Code, string Text)>();
		foreach (var reachable in _inspector.ReachableFromScript(id))
		{
			await AddRisk(reachable.IntegrationId, reachable.ActionId, details);
		}

		var steering = _inspector.SteeringScriptInputs(id);
		foreach (var name in (inputs ?? []).Keys.Where(steering.Contains))
		{
			details.Add(($"{McpConfirmationKinds.SteeringValue}: {name}", await _confirmation.SteeringValue(name)));
		}

		await Confirm(server, context, details, cancellationToken);

		var response = await _runScript.Handle(new RunScriptRequest
			{
				Id = scriptId,
				Inputs = inputs?.ToDictionary(entry => entry.Key, entry => Plain(entry.Value)),
				McpTokenId = tokenId
			},
			cancellationToken);

		return new McpRunResult(response.Status.ToString(),
			response.Error?.Code,
			await _text.Resolve(response.Error?.Message ?? default),
			response.DurationMs);
	}

	[McpServerTool(Name = "trigger_event",
		Title = "Trigger event",
		Destructive = true,
		OpenWorld = true,
		UseStructuredContent = true)]
	[Description("Fires an event so the automations and widget triggers listening to it run. Every action they "
		+ "would run is checked like run_action. Scheduled events cannot be fired by hand.")]
	public async Task<McpTriggerResult> TriggerEvent(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		[Description("The event id from the catalog.")]
		string eventId,
		[Description("Event payload values by name.")]
		Dictionary<string, JsonElement>? parameters = null,
		CancellationToken cancellationToken = default)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		var tokenId = McpConsent.TokenId(context);
		RefuseForeignSecrets(tokenId, JsonSerializer.Serialize(parameters));

		var details = new List<(string Code, string Text)>();
		foreach (var reachable in _inspector.ReachableFromEvent(eventId))
		{
			await AddRisk(reachable.IntegrationId, reachable.ActionId, details);
		}

		var steering = _inspector.SteeringEventParameters(eventId);
		foreach (var name in (parameters ?? []).Keys.Where(steering.Contains))
		{
			details.Add(($"{McpConfirmationKinds.SteeringValue}: {name}", await _confirmation.SteeringValue(name)));
		}

		await Confirm(server, context, details, cancellationToken);

		var response = await _triggerEvent.Handle(new TriggerEventRequest
			{
				EventId = eventId,
				Parameters = parameters,
				McpTokenId = tokenId
			},
			cancellationToken);
		if (!response.Success)
		{
			throw McpToolErrors.Create(response.Error?.Code ?? "TRIGGER_FAILED",
				await _text.Resolve(response.Error?.Message ?? default) ?? "The event could not be triggered.");
		}

		return new McpTriggerResult(true, response.QueuedSubscriptions);
	}

	private async Task AddRisk(
		string integrationId,
		string actionId,
		List<(string Code, string Text)> details,
		IReadOnlyDictionary<string, JsonElement>? parameters = null)
	{
		switch (await _risk.Classify(integrationId, actionId))
		{
			case McpRiskMode.Off:
				throw McpToolErrors.Create(ProviderBlocked,
					$"{integrationId}.{actionId} is turned off for AI clients.");
			case McpRiskMode.Confirm:
				details.Add(($"{McpConfirmationKinds.RiskyAction}: {integrationId}.{actionId}",
					await _confirmation.RiskyAction(integrationId, actionId, parameters)));
				break;
		}
	}

	private Task Confirm(
		McpServer server,
		RequestContext<CallToolRequestParams> context,
		List<(string Code, string Text)> details,
		CancellationToken cancellationToken)
	{
		var distinct = details.DistinctBy(detail => detail.Code, StringComparer.Ordinal).ToList();
		return _gate.Require(server,
			context,
			distinct.Select(detail => detail.Code).ToList(),
			distinct.Select(detail => detail.Text).ToList(),
			cancellationToken);
	}

	private void RefuseForeignSecrets(Guid tokenId, string serialized)
	{
		foreach (var secretId in SecretReferences.Extract(serialized))
		{
			if (!_secrets.IsGranted(tokenId, secretId))
			{
				throw McpToolErrors.Create(ForeignSecretRef,
					$"Secret {secretId} was not created by this client with set_secret.");
			}
		}
	}

	private static object? Plain(JsonElement value)
	{
		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null => null,
			_ => value.GetRawText()
		};
	}
}
