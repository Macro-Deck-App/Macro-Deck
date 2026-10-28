using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Secrets;

internal sealed class SecretApprovalFixture
{
	public const string IntegrationId = "integration";
	public const string ActionId = "send";

	public SecretApprovalFixture(IReadOnlyList<ActionParameter>? parameters = null)
	{
		Action = new CapturingActionDefinition
		{
			Id = ActionId,
			Parameters = parameters
				??
				[
					ActionParameter.Url("url"),
					ActionParameter.Secret("token"),
					ActionParameter.Object("auth",
						[ActionParameter.Text("user"), ActionParameter.Password("password")]),
					ActionParameter.Array("headers",
						ActionParameter.Object("header",
							[ActionParameter.Text("name"), ActionParameter.Secret("value")])),
					ActionParameter.Text("note")
				]
		};
		Registry.Add(new FakeIntegration { Id = IntegrationId, Actions = [Action] });
		Ledger = new SecretApprovalLedger(FindAction, Lineage, Files, Log.Logger);
		Executor = CreateExecutor(Ledger);
	}

	public FakeSecretService Secrets { get; } = new();

	public FakeIntegrationRegistry Registry { get; } = new();

	public CapturingActionDefinition Action { get; }

	public IActionDefinition? UpdatedAction { get; set; }

	public FakeSecretLineage Lineage { get; } = new();

	public InMemorySecretApprovalFiles Files { get; } = new();

	public UserNotificationStore Notifications { get; } = new();

	public SecretApprovalLedger Ledger { get; private set; }

	public FlowExecutor Executor { get; private set; }

	public bool CatalogOnline { get; set; } = true;

	public void UseLedger(SecretApprovalLedger ledger)
	{
		Ledger = ledger;
		Executor = CreateExecutor(ledger);
	}

	public IActionDefinition? FindAction(string integrationId, string actionId)
	{
		return CatalogOnline ? UpdatedAction ?? Registry.FindAction(integrationId, actionId) : null;
	}

	private FlowExecutor CreateExecutor(SecretApprovalLedger ledger)
	{
		return new FlowExecutor(Registry,
			new FakeVariableTemplateRenderer(),
			new PassthroughConditionEvaluator(),
			Secrets,
			new NullActionInteractions(),
			new NullUiInteractions(),
			Notifications,
			new MusicPlayerPollNudge(Registry),
			new FakeHostLockState(),
			TestLocalization.Preferences,
			TestLocalization.Resolver,
			Log.Logger,
			approvals: ledger);
	}

	public static string Block(string id, string parametersJson)
	{
		return $$"""
				 { "id": "{{id}}", "type": "action", "blockType": "{{IntegrationId}}.{{ActionId}}",
				   "integrationId": "{{IntegrationId}}", "actionId": "{{ActionId}}", "label": "Send",
				   "parameters": {{parametersJson}} }
				 """;
	}

	public static string Flows(params string[] blocks)
	{
		return
			$$"""[{ "triggerId": "t1", "triggerType": "onShortPress", "children": [{{string.Join(",", blocks)}}] }]""";
	}

	public static string Ref(Guid id)
	{
		return $$"""{ "$secret": "{{id}}" }""";
	}

	public void Record(FlowOwner owner, string flows, SecretApprovalWrite write)
	{
		Ledger.Record(owner, FlowSecretBlocks.FromFlows(flows), write);
	}

	public async Task<FlowExecutionResult> Run(FlowOwner? owner, string flows, bool trustedDraft = false)
	{
		return await Executor.ExecuteAsync(new FlowExecutionRequest
			{
				FlowsSource = JsonSerializer.Serialize(new Dictionary<string, string> { ["flows"] = flows }),
				Trigger = TriggerSelector.ByType("onShortPress"),
				Scope = VariableScope.Widget,
				Owner = owner,
				TrustedDraft = trustedDraft,
				Origin = ExecutionOrigin.Host
			},
			CancellationToken.None);
	}

	public bool Approve(FlowOwner owner, string flows, string blockId)
	{
		var block = FlowSecretBlocks.FromFlows(flows).Single(candidate => candidate.Id == blockId);
		return Ledger.Approve(owner, block, Ledger.Fingerprint(block.Content));
	}

	private sealed class NullActionInteractions : IActionInteractions
	{
		public void RequestItemPicker(
			string? originClientId,
			string instanceId,
			MusicPlayerCatalogItemKind kind,
			string? prompt = null)
		{
		}

		public void RequestDevicePicker(
			string? originClientId,
			string instanceId,
			bool startPlayback,
			string? prompt = null)
		{
		}
	}
}

internal sealed class FakeSecretLineage : ISecretLineage
{
	private readonly Dictionary<Guid, Guid> _parents = [];

	public Guid Root(Guid secretId)
	{
		var current = secretId;
		while (_parents.TryGetValue(current, out var parent))
		{
			current = parent;
		}

		return current;
	}

	public void RecordClone(Guid sourceId, Guid cloneId)
	{
		_parents[cloneId] = sourceId;
	}
}

internal sealed class InMemorySecretApprovalFiles : ISecretApprovalFiles
{
	public HashSet<SecretApprovalGroup> Backfilled { get; } = [];

	public Dictionary<(SecretApprovalGroup Group, Guid PartitionId), string> Saved { get; } = [];

	public HashSet<(SecretApprovalGroup Group, Guid PartitionId)> Unreadable { get; } = [];

	public bool IsBackfilled(SecretApprovalGroup group)
	{
		return Backfilled.Contains(group);
	}

	public void MarkBackfilled(SecretApprovalGroup group)
	{
		Backfilled.Add(group);
	}

	public IReadOnlyList<SecretApprovalPartitionLoad> LoadAll(SecretApprovalGroup group)
	{
		var loads = Saved
			.Where(entry => entry.Key.Group == group)
			.Select(entry => new SecretApprovalPartitionLoad(entry.Key.PartitionId,
				JsonSerializer.Deserialize<SecretApprovalPartition>(entry.Value),
				false))
			.ToList();
		loads.AddRange(Unreadable
			.Where(entry => entry.Group == group)
			.Select(entry => new SecretApprovalPartitionLoad(entry.PartitionId, null, true)));
		return loads;
	}

	public void Save(SecretApprovalGroup group, Guid partitionId, SecretApprovalPartition partition)
	{
		Saved[(group, partitionId)] = JsonSerializer.Serialize(partition);
	}

	public void Delete(SecretApprovalGroup group, Guid partitionId)
	{
		Saved.Remove((group, partitionId));
	}
}
