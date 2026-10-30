using System.ComponentModel;
using System.Text.Json;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using ModelContextProtocol.Server;

namespace MacroDeckHost.Mcp.Tools;

[McpServerToolType]
public sealed class McpEntityTools
{
	private readonly IUiTransportMessageHandler<GetProfilesRequest, GetProfilesResponse> _getProfiles;
	private readonly IUiTransportMessageHandler<GetFoldersRequest, GetFoldersResponse> _getFolders;
	private readonly IUiTransportMessageHandler<GetAutomationsRequest, GetAutomationsResponse> _getAutomations;
	private readonly IUiTransportMessageHandler<GetScriptsRequest, GetScriptsResponse> _getScripts;
	private readonly IUiTransportMessageHandler<GetVariablesRequest, GetVariablesResponse> _getVariables;
	private readonly ISecretApprovals _approvals;

	public McpEntityTools(
		IUiTransportMessageHandler<GetProfilesRequest, GetProfilesResponse> getProfiles,
		IUiTransportMessageHandler<GetFoldersRequest, GetFoldersResponse> getFolders,
		IUiTransportMessageHandler<GetAutomationsRequest, GetAutomationsResponse> getAutomations,
		IUiTransportMessageHandler<GetScriptsRequest, GetScriptsResponse> getScripts,
		IUiTransportMessageHandler<GetVariablesRequest, GetVariablesResponse> getVariables,
		ISecretApprovals approvals)
	{
		_approvals = approvals;
		_getProfiles = getProfiles;
		_getFolders = getFolders;
		_getAutomations = getAutomations;
		_getScripts = getScripts;
		_getVariables = getVariables;
	}

	[McpServerTool(Name = "list", Title = "List", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
	[Description("Lists profiles, folders, widgets, automations, scripts or variables. Widgets and folders can be "
		+ "narrowed to a profile, widgets also to a folder. Secrets appear only as {\"$secret\":\"<id>\"} "
		+ "references, never as values.")]
	public async Task<McpListResult> List(
		[Description("profiles, folders, widgets, automations, scripts or variables.")]
		string entity,
		[Description("Optional: only folders or widgets of this profile.")]
		string? profileId = null,
		[Description("Optional: only widgets of this folder.")]
		string? folderId = null,
		CancellationToken cancellationToken = default)
	{
		var kind = McpEntityKinds.Normalize(entity);
		IReadOnlyList<JsonElement> items = kind switch
		{
			McpEntityKinds.Profiles => (await Profiles(cancellationToken)).Select(McpJson.ToElement).ToList(),
			McpEntityKinds.Folders => (await Folders(profileId, cancellationToken))
				.Select(folder => McpJson.ToElement(FolderItem(folder)))
				.ToList(),
			McpEntityKinds.Widgets => (await Folders(profileId, cancellationToken))
				.Where(folder => folderId is null || folder.Id == folderId)
				.SelectMany(folder => folder.Widgets.Select(widget => WidgetItem(folder.Id, widget)))
				.Select(McpJson.ToElement)
				.ToList(),
			McpEntityKinds.Automations => (await Automations(cancellationToken))
				.Select(automation => McpJson.ToElement(automation with { Flows = null }))
				.ToList(),
			McpEntityKinds.Scripts => (await Scripts(cancellationToken))
				.Select(script => McpJson.ToElement(script with { Flows = null }))
				.ToList(),
			_ => (await Variables(cancellationToken)).Select(McpJson.ToElement).ToList()
		};

		return new McpListResult(kind, items);
	}

	[McpServerTool(Name = "get", Title = "Get", ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
	[Description("Returns one profile, folder, widget (with its data and action flows), automation or script (with "
		+ "flows), or variable. Secrets appear only as {\"$secret\":\"<id>\"} references.")]
	public async Task<McpGetResult> Get(
		[Description("profiles, folders, widgets, automations, scripts or variables.")]
		string entity,
		[Description("The id from list.")] string id,
		CancellationToken cancellationToken = default)
	{
		var kind = McpEntityKinds.Normalize(entity);
		object? item = kind switch
		{
			McpEntityKinds.Profiles => (await Profiles(cancellationToken)).FirstOrDefault(profile => profile.Id == id),
			McpEntityKinds.Folders => (await Folders(null, cancellationToken))
				.Where(folder => folder.Id == id)
				.Select(FolderItem)
				.FirstOrDefault(),
			McpEntityKinds.Widgets => (await Folders(null, cancellationToken))
				.SelectMany(folder => folder.Widgets.Select(widget => WidgetItem(folder.Id, widget)))
				.FirstOrDefault(widget => widget.Id == id),
			McpEntityKinds.Automations => (await Automations(cancellationToken))
				.FirstOrDefault(automation => automation.Id == id),
			McpEntityKinds.Scripts => (await Scripts(cancellationToken)).FirstOrDefault(script => script.Id == id),
			_ => (await Variables(cancellationToken)).FirstOrDefault(variable => variable.Id == id)
		};

		if (item is null)
		{
			throw McpToolErrors.Create(McpToolErrors.NotFound, $"No {kind} entry with id '{id}'.");
		}

		return new McpGetResult(kind, McpJson.ToElement(item), SecretBlocks(item));
	}

	private List<McpSecretBlockStatus> SecretBlocks(object item)
	{
		var (owner, blocks) = item switch
		{
			McpWidgetItem widget when Guid.TryParse(widget.Id, out var id) => (FlowOwner.Widget(id),
				FlowSecretBlocks.FromWidgetData(widget.Data?.GetRawText())),
			McpAutomationItem automation when Guid.TryParse(automation.Id, out var id) => (FlowOwner.Automation(id),
				FlowSecretBlocks.FromFlows(automation.Flows?.GetRawText())),
			McpScriptItem script when Guid.TryParse(script.Id, out var id) => (FlowOwner.Script(id),
				FlowSecretBlocks.FromFlows(script.Flows?.GetRawText())),
			_ => (default(FlowOwner?), (IReadOnlyList<FlowBlockSnapshot>)[])
		};

		return owner is { } known
			? blocks.Select(block
					=> new McpSecretBlockStatus(block.Id, _approvals.IsApproved(known, block.Id, block.Content)))
				.ToList()
			: [];
	}

	private async Task<IReadOnlyList<McpProfileItem>> Profiles(CancellationToken cancellationToken)
	{
		var response = await _getProfiles.Handle(new GetProfilesRequest(), cancellationToken);
		return response.Profiles
			.Where(profile => !profile.IsVirtual)
			.Select(profile => new McpProfileItem(profile.Id,
				profile.Name,
				profile.Order,
				profile.DefaultRows,
				profile.DefaultColumns))
			.ToList();
	}

	private async Task<IReadOnlyList<Folder>> Folders(string? profileId, CancellationToken cancellationToken)
	{
		var response = await _getFolders.Handle(new GetFoldersRequest { ProfileId = profileId }, cancellationToken);
		return response.Folders;
	}

	private async Task<IReadOnlyList<McpAutomationItem>> Automations(CancellationToken cancellationToken)
	{
		var response = await _getAutomations.Handle(new GetAutomationsRequest(), cancellationToken);
		return response.Automations
			.Select(automation => new McpAutomationItem(automation.Id,
				automation.Name,
				automation.Description,
				automation.Enabled,
				McpJson.ParseOrNull(automation.Flows)))
			.ToList();
	}

	private async Task<IReadOnlyList<McpScriptItem>> Scripts(CancellationToken cancellationToken)
	{
		var response = await _getScripts.Handle(new GetScriptsRequest(), cancellationToken);
		return response.Scripts
			.Select(script => new McpScriptItem(script.Id,
				script.Name,
				script.Description,
				script.RunsOnWidget,
				McpJson.ToElement(script.Inputs),
				McpJson.ParseOrNull(script.Flows)))
			.ToList();
	}

	private async Task<IReadOnlyList<McpVariableItem>> Variables(CancellationToken cancellationToken)
	{
		var response = await _getVariables.Handle(new GetVariablesRequest(), cancellationToken);
		return response.Variables
			.Select(variable => new McpVariableItem(variable.Id,
				variable.Name,
				variable.Scope,
				variable.ScopeRefId,
				variable.Type,
				variable.Classification,
				variable.OwnerIntegrationId,
				variable.Value,
				variable.CanWrite))
			.ToList();
	}

	private static McpFolderItem FolderItem(Folder folder)
	{
		return new McpFolderItem(folder.Id,
			folder.Name,
			folder.ProfileId,
			folder.ParentId,
			folder.Order,
			folder.Rows,
			folder.Columns,
			folder.IsDefault,
			folder.Widgets.Count);
	}

	private static McpWidgetItem WidgetItem(string folderId, Widget widget)
	{
		return new McpWidgetItem(widget.Id,
			folderId,
			widget.Type,
			widget.PositionX,
			widget.PositionY,
			widget.Width,
			widget.Height,
			McpJson.ParseOrNull(widget.Data));
	}
}
