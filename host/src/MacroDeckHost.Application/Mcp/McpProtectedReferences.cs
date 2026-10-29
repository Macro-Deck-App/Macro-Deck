using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Secrets.Approvals;

namespace MacroDeckHost.Application.Mcp;

public interface IMcpProtectedReferences
{
	IReadOnlySet<string> ProtectedVariables();

	McpBlockReferences OfOwner(FlowOwner owner, string? flowsJson);
}

public sealed class McpProtectedReferences : IMcpProtectedReferences
{
	private readonly IFolderCache _folders;
	private readonly IAutomationCache _automations;
	private readonly IScriptCache _scripts;
	private readonly ISecretApprovals _approvals;

	public McpProtectedReferences(
		IFolderCache folders,
		IAutomationCache automations,
		IScriptCache scripts,
		ISecretApprovals approvals)
	{
		_folders = folders;
		_automations = automations;
		_scripts = scripts;
		_approvals = approvals;
	}

	public IReadOnlySet<string> ProtectedVariables()
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach (var widget in _folders.GetAllFolders().SelectMany(folder => folder.Widgets))
		{
			names.UnionWith(Approved(FlowOwner.Widget(widget.Id), FlowSecretBlocks.FromWidgetData(widget.Data))
				.Variables);
		}

		foreach (var automation in _automations.GetAll())
		{
			names.UnionWith(OfOwner(FlowOwner.Automation(automation.Id), automation.Flows).Variables);
		}

		foreach (var script in _scripts.GetAll())
		{
			names.UnionWith(OfOwner(FlowOwner.Script(script.Id), script.Flows).Variables);
		}

		return names;
	}

	public McpBlockReferences OfOwner(FlowOwner owner, string? flowsJson)
	{
		return Approved(owner, FlowSecretBlocks.FromFlows(flowsJson));
	}

	private McpBlockReferences Approved(FlowOwner owner, IReadOnlyList<FlowBlockSnapshot> blocks)
	{
		var variables = new HashSet<string>(StringComparer.Ordinal);
		var events = new HashSet<string>(StringComparer.Ordinal);
		foreach (var block in blocks.Where(block => _approvals.IsApproved(owner, block.Id, block.Content)))
		{
			var references = McpTemplateReferences.OfNonSecretParameters(block.Content);
			variables.UnionWith(references.Variables);
			events.UnionWith(references.EventParameters);
		}

		return new McpBlockReferences(variables, events);
	}
}
