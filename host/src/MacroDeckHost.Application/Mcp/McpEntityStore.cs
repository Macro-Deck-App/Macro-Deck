using System.Text.Json;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Mcp;

public interface IMcpEntityStore
{
	Task<McpJournalItem?> Capture(string entity, Guid id, McpJournalOperation operation);

	Task<string?> CurrentFingerprint(string entity, Guid id);

	Dictionary<Guid, bool>? StartFoldersAfter(McpJournalItem item);

	Task<McpRestoreResult> Restore(McpJournalItem item);
}

public enum McpRestoreResult
{
	Restored,
	Failed,
	StartFolderKept
}

public sealed record McpFolderSubtree(List<FolderEntity> Folders, Dictionary<Guid, bool> StartFolders);

public sealed class McpEntityStore : IMcpEntityStore
{
	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.General);

	private readonly IFolderCache _folders;
	private readonly IAutomationCache _automations;
	private readonly IScriptCache _scripts;
	private readonly IProfileCache _profiles;
	private readonly IVariableService _variables;
	private readonly ISecretApprovals _approvals;
	private readonly IMediator _mediator;

	public McpEntityStore(
		IFolderCache folders,
		IAutomationCache automations,
		IScriptCache scripts,
		IProfileCache profiles,
		IVariableService variables,
		ISecretApprovals approvals,
		IMediator mediator)
	{
		_folders = folders;
		_automations = automations;
		_scripts = scripts;
		_profiles = profiles;
		_variables = variables;
		_approvals = approvals;
		_mediator = mediator;
	}

	public async Task<McpJournalItem?> Capture(string entity, Guid id, McpJournalOperation operation)
	{
		var snapshot = entity == McpEntities.Folder && operation == McpJournalOperation.Deleted
			? SubtreeSnapshot(id)
			: await Snapshot(entity, id);
		if (snapshot is null && operation != McpJournalOperation.Created)
		{
			return null;
		}

		var item = new McpJournalItem { Entity = entity, Id = id, Operation = operation, Before = snapshot };
		if (operation == McpJournalOperation.Created)
		{
			return item;
		}

		item.ApprovedSnapshots.AddRange(ApprovedSnapshots(entity, id));
		if (operation == McpJournalOperation.Deleted)
		{
			foreach (var variable in await CascadedVariables(entity, id))
			{
				item.CascadedVariables.Add(JsonSerializer.SerializeToElement(variable, _json));
			}
		}

		return item;
	}

	public async Task<string?> CurrentFingerprint(string entity, Guid id)
	{
		if (entity == McpEntities.Folder)
		{
			return _folders.GetFolderById(id) is { } folder
				? McpJournal.Fingerprint(JsonSerializer.SerializeToElement(FolderWithoutWidgets(folder), _json))
				: null;
		}

		var snapshot = await Snapshot(entity, id);
		return snapshot is null ? null : McpJournal.Fingerprint(snapshot);
	}

	private static FolderEntity FolderWithoutWidgets(FolderEntity folder)
	{
		var copy = JsonSerializer.Deserialize<FolderEntity>(JsonSerializer.Serialize(folder, _json), _json)!;
		copy.Widgets = [];
		return copy;
	}

	public Dictionary<Guid, bool>? StartFoldersAfter(McpJournalItem item)
	{
		if (item is not { Entity: McpEntities.Folder, Operation: McpJournalOperation.Deleted, Before: { } before }
			|| before.Deserialize<McpFolderSubtree>(_json) is not { Folders.Count: > 0 } subtree)
		{
			return null;
		}

		return StartFlags(subtree.Folders[0].ProfileId);
	}

	public async Task<McpRestoreResult> Restore(McpJournalItem item)
	{
		if (item.Before is not { } before)
		{
			return McpRestoreResult.Failed;
		}

		if (item.Entity == McpEntities.Folder && item.Operation == McpJournalOperation.Deleted)
		{
			var outcome = await RestoreSubtree(before.Deserialize<McpFolderSubtree>(_json)!,
				item.StartFoldersAfter,
				SecretApprovalWrite.Restore(item.ApprovedSnapshots));
			if (outcome != McpRestoreResult.Failed)
			{
				await RestoreCascadedVariables(item);
			}

			return outcome;
		}

		var write = SecretApprovalWrite.Restore(item.ApprovedSnapshots);
		var restored = item.Entity switch
		{
			McpEntities.Widget => await RestoreWidget(before.Deserialize<WidgetEntity>(_json)!, item.Operation, write),
			McpEntities.Folder => await RestoreFolder(before.Deserialize<FolderEntity>(_json)!, write),
			McpEntities.Automation => await RestoreAutomation(before.Deserialize<AutomationEntity>(_json)!,
				item.Operation,
				write),
			McpEntities.Script => await RestoreScript(before.Deserialize<ScriptEntity>(_json)!, item.Operation, write),
			McpEntities.Variable => await RestoreVariable(before.Deserialize<VariableEntity>(_json)!, item.Operation),
			McpEntities.Profile => await RestoreProfile(before.Deserialize<ProfileEntity>(_json)!),
			_ => false
		};

		if (!restored)
		{
			return McpRestoreResult.Failed;
		}

		await RestoreCascadedVariables(item);
		return McpRestoreResult.Restored;
	}

	private async Task RestoreCascadedVariables(McpJournalItem item)
	{
		foreach (var variable in item.CascadedVariables)
		{
			await _variables.RestoreUserVariable(variable.Deserialize<VariableEntity>(_json)!);
		}
	}

	private async Task<JsonElement?> Snapshot(string entity, Guid id)
	{
		object? value = entity switch
		{
			McpEntities.Widget => FindWidget(id),
			McpEntities.Folder => _folders.GetFolderById(id),
			McpEntities.Automation => _automations.GetById(id),
			McpEntities.Script => _scripts.GetById(id),
			McpEntities.Variable => await _variables.GetById(id),
			McpEntities.Profile => _profiles.GetById(id),
			_ => null
		};

		return value is null ? null : JsonSerializer.SerializeToElement(value, value.GetType(), _json);
	}

	private IEnumerable<string> ApprovedSnapshots(string entity, Guid id)
	{
		IEnumerable<(FlowOwner Owner, IReadOnlyList<FlowBlockSnapshot> Blocks)> owners = entity switch
		{
			McpEntities.Widget when FindWidget(id) is { } widget =>
				[(FlowOwner.Widget(widget.Id), FlowSecretBlocks.FromWidgetData(widget.Data))],
			McpEntities.Folder when _folders.GetFolderById(id) is { } folder => Subtree(folder)
				.SelectMany(member => member.Widgets)
				.Select(widget => (FlowOwner.Widget(widget.Id), FlowSecretBlocks.FromWidgetData(widget.Data))),
			McpEntities.Automation when _automations.GetById(id) is { } automation =>
				[(FlowOwner.Automation(id), FlowSecretBlocks.FromFlows(automation.Flows))],
			McpEntities.Script when _scripts.GetById(id) is { } script =>
				[(FlowOwner.Script(id), FlowSecretBlocks.FromFlows(script.Flows))],
			_ => []
		};

		return owners.SelectMany(owner => owner.Blocks
			.Where(block => _approvals.IsApproved(owner.Owner, block.Id, block.Content))
			.Select(block => block.Raw));
	}

	private async Task<IReadOnlyList<VariableEntity>> CascadedVariables(string entity, Guid id)
	{
		var scopes = entity switch
		{
			McpEntities.Widget => [(VariableScope.Widget, id)],
			McpEntities.Folder => (_folders.GetFolderById(id) is { } folder ? Subtree(folder) : [])
				.SelectMany(member => member.Widgets)
				.Select(widget => (VariableScope.Widget, widget.Id))
				.ToList(),
			McpEntities.Automation => [(VariableScope.Automation, id)],
			McpEntities.Script => [(VariableScope.Script, id)],
			_ => new List<(VariableScope, Guid)>()
		};

		var result = new List<VariableEntity>();
		foreach (var (scope, owner) in scopes)
		{
			result.AddRange((await _variables.GetByScope(scope, owner.ToString()))
				.Where(variable => variable.Classification == VariableClassification.User));
		}

		return result;
	}

	private JsonElement? SubtreeSnapshot(Guid id)
	{
		if (_folders.GetFolderById(id) is not { } root)
		{
			return null;
		}

		return JsonSerializer.SerializeToElement(new McpFolderSubtree(Subtree(root), StartFlags(root.ProfileId)),
			_json);
	}

	private Dictionary<Guid, bool> StartFlags(Guid profileId)
	{
		return _folders.GetFoldersByProfileId(profileId).ToDictionary(folder => folder.Id, folder => folder.IsDefault);
	}

	private List<FolderEntity> Subtree(FolderEntity root)
	{
		return FolderSubtree.Collect(_folders.GetFoldersByProfileId(root.ProfileId), root);
	}

	private async Task<McpRestoreResult> RestoreSubtree(
		McpFolderSubtree subtree,
		Dictionary<Guid, bool>? leftStartFolders,
		SecretApprovalWrite write)
	{
		if (subtree.Folders.Count == 0 || _profiles.GetById(subtree.Folders[0].ProfileId) is null)
		{
			return McpRestoreResult.Failed;
		}

		var remaining = _folders.GetFoldersByProfileId(subtree.Folders[0].ProfileId);
		var changedSinceBatch = leftStartFolders is null
			|| remaining.Any(folder => folder.IsDefault != leftStartFolders.GetValueOrDefault(folder.Id))
			|| leftStartFolders.Any(left => left.Value && remaining.All(folder => folder.Id != left.Key));
		var wouldChange = subtree.Folders.Any(folder => folder.IsDefault)
			|| remaining.Any(folder => subtree.StartFolders.TryGetValue(folder.Id, out var wasDefault)
				&& wasDefault != folder.IsDefault);

		var restoredStart = subtree.Folders.Where(folder => folder.IsDefault).ToList();
		foreach (var folder in restoredStart)
		{
			folder.IsDefault = false;
		}

		await _folders.AddOrUpdateRange(subtree.Folders, write);

		// One write for every flag, otherwise the start folder invariant keeps the oldest marked folder.
		List<FolderEntity> changedStartFolders = [];
		if (!changedSinceBatch)
		{
			changedStartFolders = remaining.Where(folder
					=> subtree.StartFolders.TryGetValue(folder.Id, out var wasDefault)
					&& wasDefault != folder.IsDefault)
				.ToList();
			foreach (var folder in changedStartFolders.Concat(restoredStart))
			{
				folder.IsDefault = subtree.StartFolders.GetValueOrDefault(folder.Id, true);
			}

			if (changedStartFolders.Count > 0 || restoredStart.Count > 0)
			{
				await _folders.AddOrUpdateRange(changedStartFolders.Concat(restoredStart).ToList(),
					SecretApprovalWrite.Preserve);
			}
		}

		foreach (var folder in subtree.Folders)
		{
			await _mediator.Publish(new FolderCreatedNotification(folder));
		}

		foreach (var widget in subtree.Folders.SelectMany(folder => folder.Widgets))
		{
			await _mediator.Publish(new WidgetCreatedNotification(widget));
		}

		foreach (var folder in changedStartFolders)
		{
			await _mediator.Publish(new FolderUpdatedNotification(folder));
		}

		return changedSinceBatch && wouldChange ? McpRestoreResult.StartFolderKept : McpRestoreResult.Restored;
	}

	private WidgetEntity? FindWidget(Guid id)
	{
		return _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(widget => widget.Id == id);
	}

	private async Task<bool> RestoreWidget(
		WidgetEntity widget,
		McpJournalOperation operation,
		SecretApprovalWrite write)
	{
		if (_folders.GetFolderById(widget.FolderId) is null)
		{
			return false;
		}

		if (operation == McpJournalOperation.Deleted)
		{
			_folders.AddWidget(widget.FolderId, widget, write);
			await _mediator.Publish(new WidgetCreatedNotification(widget));
		}
		else
		{
			_folders.UpdateWidget(widget.FolderId, widget, write);
			await _mediator.Publish(new WidgetUpdatedNotification(widget));
		}

		return true;
	}

	private async Task<bool> RestoreFolder(FolderEntity folder, SecretApprovalWrite write)
	{
		if (_profiles.GetById(folder.ProfileId) is null)
		{
			return false;
		}

		if (_folders.GetFolderById(folder.Id) is { } current)
		{
			folder.Widgets = current.Widgets;
		}

		await _folders.AddOrUpdate(folder, write);
		await _mediator.Publish(new FolderUpdatedNotification(folder));
		return true;
	}

	private async Task<bool> RestoreAutomation(
		AutomationEntity automation,
		McpJournalOperation operation,
		SecretApprovalWrite write)
	{
		await _automations.AddOrUpdate(automation, write);
		await _mediator.Publish(operation == McpJournalOperation.Deleted
			? new AutomationCreatedNotification(automation)
			: new AutomationUpdatedNotification(automation));
		return true;
	}

	private async Task<bool> RestoreScript(
		ScriptEntity script,
		McpJournalOperation operation,
		SecretApprovalWrite write)
	{
		await _scripts.AddOrUpdate(script, write);
		await _mediator.Publish(operation == McpJournalOperation.Deleted
			? new ScriptCreatedNotification(script)
			: new ScriptUpdatedNotification(script));
		return true;
	}

	private async Task<bool> RestoreVariable(VariableEntity variable, McpJournalOperation operation)
	{
		if (operation == McpJournalOperation.Deleted)
		{
			return await _variables.RestoreUserVariable(variable);
		}

		var result = await _variables.SetValue(variable.Id, variable.Value);
		return result.Success;
	}

	private async Task<bool> RestoreProfile(ProfileEntity profile)
	{
		if (_profiles.GetById(profile.Id) is null)
		{
			return false;
		}

		await _profiles.AddOrUpdate(profile);
		await _mediator.Publish(new ProfileUpdatedNotification(profile));
		return true;
	}
}
