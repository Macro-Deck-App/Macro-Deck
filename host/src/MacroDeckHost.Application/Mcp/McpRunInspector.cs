using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Triggers;

namespace MacroDeckHost.Application.Mcp;

public sealed record McpReachableAction(string IntegrationId, string ActionId, string Owner);

public interface IMcpRunInspector
{
	IReadOnlyList<McpReachableAction> ReachableFromScript(Guid scriptId);

	IReadOnlyList<McpReachableAction> ReachableFromEvent(string eventId);

	IReadOnlySet<string> SteeringScriptInputs(Guid scriptId);

	IReadOnlySet<string> SteeringEventParameters(string eventId);
}

public sealed class McpRunInspector : IMcpRunInspector
{
	public const string ScriptsIntegrationId = "app.macro-deck.scripts";
	public const string RunScriptActionId = "run-script";
	private const string ScriptIdParameter = "scriptId";
	public const string InputPrefix = "input:";

	private readonly IFolderCache _folders;
	private readonly IAutomationCache _automations;
	private readonly IScriptCache _scripts;
	private readonly IEventSubscriptionIndex _subscriptions;
	private readonly IMcpProtectedReferences _references;

	public McpRunInspector(
		IFolderCache folders,
		IAutomationCache automations,
		IScriptCache scripts,
		IEventSubscriptionIndex subscriptions,
		IMcpProtectedReferences references)
	{
		_folders = folders;
		_automations = automations;
		_scripts = scripts;
		_subscriptions = subscriptions;
		_references = references;
	}

	public IReadOnlyList<McpReachableAction> ReachableFromScript(Guid scriptId)
	{
		var result = new List<McpReachableAction>();
		CollectScript(scriptId, [], result);
		return result;
	}

	public IReadOnlyList<McpReachableAction> ReachableFromEvent(string eventId)
	{
		var result = new List<McpReachableAction>();
		foreach (var (_, owner, flows) in SubscribedFlows(eventId))
		{
			CollectFlows(flows, owner, [], result);
		}

		return result;
	}

	public IReadOnlySet<string> SteeringScriptInputs(Guid scriptId)
	{
		return SteeringInputs(scriptId, []);
	}

	public IReadOnlySet<string> SteeringEventParameters(string eventId)
	{
		var steering = new HashSet<string>(StringComparer.Ordinal);
		foreach (var (flowOwner, _, flows) in SubscribedFlows(eventId))
		{
			steering.UnionWith(_references.OfOwner(flowOwner, flows).EventParameters);
			foreach (var (target, input, references) in NestedScriptCalls(flows))
			{
				if (SteeringInputs(target, []).Contains(input))
				{
					steering.UnionWith(references.EventParameters);
				}
			}
		}

		return steering;
	}

	private HashSet<string> SteeringInputs(Guid scriptId, HashSet<Guid> visited)
	{
		var steering = new HashSet<string>(StringComparer.Ordinal);
		if (!visited.Add(scriptId) || _scripts.GetById(scriptId) is not { } script)
		{
			return steering;
		}

		steering.UnionWith(_references.OfOwner(FlowOwner.Script(scriptId), script.Flows).Variables);
		foreach (var (target, input, references) in NestedScriptCalls(script.Flows))
		{
			if (SteeringInputs(target, visited).Contains(input))
			{
				steering.UnionWith(references.Variables);
			}
		}

		return steering;
	}

	private void CollectScript(Guid scriptId, HashSet<Guid> visited, List<McpReachableAction> result)
	{
		if (visited.Add(scriptId) && _scripts.GetById(scriptId) is { } script)
		{
			CollectFlows(script.Flows, $"script {script.Name}", visited, result);
		}
	}

	private void CollectFlows(string? flows, string owner, HashSet<Guid> visited, List<McpReachableAction> result)
	{
		foreach (var block in FlowSecretBlocks.ActionBlocksOfFlows(flows))
		{
			if (block.Content is not { IntegrationId: { } integrationId, ActionId: { } actionId })
			{
				continue;
			}

			result.Add(new McpReachableAction(integrationId, actionId, owner));
			if (ScriptTarget(block.Content) is { } target)
			{
				CollectScript(target, visited, result);
			}
		}
	}

	private IEnumerable<(FlowOwner Owner, string Label, string? Flows)> SubscribedFlows(string eventId)
	{
		foreach (var subscription in _subscriptions.Find(eventId))
		{
			if (subscription.Owner.Kind == EventTriggerOwnerKind.Automation)
			{
				if (_automations.GetById(subscription.Owner.Id) is { } automation)
				{
					yield return (FlowOwner.Automation(automation.Id), $"automation {automation.Name}",
						automation.Flows);
				}
			}
			else if (_folders.GetAllFolders()
					.SelectMany(folder => folder.Widgets)
					.FirstOrDefault(widget => widget.Id == subscription.Owner.Id) is { } widget
				&& WidgetFlowsJson.TryExtract(widget.Data, out var flows))
			{
				yield return (FlowOwner.Widget(widget.Id), $"widget {widget.Id}", flows);
			}
		}
	}

	private static IEnumerable<(Guid Target, string Input, McpBlockReferences References)> NestedScriptCalls(
		string? flows)
	{
		foreach (var block in FlowSecretBlocks.ActionBlocksOfFlows(flows))
		{
			if (ScriptTarget(block.Content) is not { } target)
			{
				continue;
			}

			foreach (var parameter in block.Content.Parameters.Where(parameter =>
				parameter.Name.StartsWith(InputPrefix, StringComparison.Ordinal)))
			{
				var single = new FlowBlockContent(block.Content.IntegrationId, block.Content.ActionId, [parameter]);
				yield return (target, parameter.Name[InputPrefix.Length..],
					McpTemplateReferences.OfNonSecretParameters(single));
			}
		}
	}

	public static Guid? ScriptTarget(FlowBlockContent block)
	{
		if (block is not { IntegrationId: ScriptsIntegrationId, ActionId: RunScriptActionId })
		{
			return null;
		}

		var parameter = block.Parameters.FirstOrDefault(candidate => candidate.Name == ScriptIdParameter);
		return parameter?.Value.ValueKind == JsonValueKind.String
			&& Guid.TryParse(parameter.Value.GetString(), out var id)
				? id
				: null;
	}
}
