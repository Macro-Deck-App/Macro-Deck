using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Automations;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Mcp;

public interface IMcpChangeSet
{
	Task<McpChangeValidation> Validate(Guid tokenId, IReadOnlyList<McpChange> changes);

	Task<McpChangeResult> Apply(Guid tokenId, IReadOnlyList<McpChange> changes, string summary);

	Task<IReadOnlyList<McpProblem>> Undo(Guid batchId);
}

public sealed class McpChangeSet : IMcpChangeSet
{
	private const string RefPrefix = "@";

	private static readonly SemaphoreSlim _writes = new(1, 1);

	private readonly IServiceProvider _services;
	private readonly IFolderCache _folders;
	private readonly IAutomationCache _automations;
	private readonly IScriptCache _scripts;
	private readonly IProfileCache _profiles;
	private readonly IVariableService _variables;
	private readonly IIntegrationRegistry _integrations;
	private readonly IEventRegistry _events;
	private readonly IWidgetDataSchemaProvider _schemas;
	private readonly ISecretApprovals _approvals;
	private readonly IMcpRiskPolicy _risk;
	private readonly IMcpProtectedReferences _references;
	private readonly IMcpRunInspector _inspector;
	private readonly IMcpSecretLedger _secrets;
	private readonly IMcpEntityStore _entities;
	private readonly IMcpJournal _journal;
	private readonly SecretWriteAuthority _authority;
	private readonly SecretScrubDeferral _deferral;
	private readonly ILocalizationResolver _localization;

	public McpChangeSet(
		IServiceProvider services,
		IFolderCache folders,
		IAutomationCache automations,
		IScriptCache scripts,
		IProfileCache profiles,
		IVariableService variables,
		IIntegrationRegistry integrations,
		IEventRegistry events,
		IWidgetDataSchemaProvider schemas,
		ISecretApprovals approvals,
		IMcpRiskPolicy risk,
		IMcpProtectedReferences references,
		IMcpRunInspector inspector,
		IMcpSecretLedger secrets,
		IMcpEntityStore entities,
		IMcpJournal journal,
		SecretWriteAuthority authority,
		SecretScrubDeferral deferral,
		ILocalizationResolver localization)
	{
		_services = services;
		_folders = folders;
		_automations = automations;
		_scripts = scripts;
		_profiles = profiles;
		_variables = variables;
		_integrations = integrations;
		_events = events;
		_schemas = schemas;
		_approvals = approvals;
		_risk = risk;
		_references = references;
		_inspector = inspector;
		_secrets = secrets;
		_entities = entities;
		_journal = journal;
		_authority = authority;
		_deferral = deferral;
		_localization = localization;
	}

	public async Task<McpChangeValidation> Validate(Guid tokenId, IReadOnlyList<McpChange> changes)
	{
		var problems = new List<McpProblem>();
		var confirmations = new List<McpConfirmation>();
		var declared = new Dictionary<string, string>(StringComparer.Ordinal);
		var protectedVariables = _references.ProtectedVariables();

		for (var index = 0; index < changes.Count; index++)
		{
			var change = changes[index];
			var context = new ValidationContext(index, tokenId, problems, confirmations, declared, protectedVariables);
			await ValidateOne(change, context);
			if (change is { Op: McpChangeOps.Create, Ref: { Length: > 0 } reference })
			{
				if (!declared.TryAdd(reference, change.Entity))
				{
					problems.Add(new McpProblem(index,
						"ref",
						McpProblemCodes.InvalidArgument,
						$"ref '{reference}' is declared twice."));
				}
			}
		}

		return new McpChangeValidation(problems, confirmations);
	}

	public async Task<McpChangeResult> Apply(Guid tokenId, IReadOnlyList<McpChange> changes, string summary)
	{
		await _writes.WaitAsync();
		try
		{
			return await ApplyLocked(tokenId, changes, summary);
		}
		finally
		{
			_writes.Release();
		}
	}

	public async Task<IReadOnlyList<McpProblem>> Undo(Guid batchId)
	{
		await _writes.WaitAsync();
		try
		{
			return await UndoLocked(batchId);
		}
		finally
		{
			_writes.Release();
		}
	}

	private async Task<McpChangeResult> ApplyLocked(Guid tokenId, IReadOnlyList<McpChange> changes, string summary)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		_deferral.Activate();

		var created = new Dictionary<string, string>(StringComparer.Ordinal);
		var batch = new McpJournalBatch
		{
			BatchId = Guid.NewGuid(),
			At = DateTime.UtcNow,
			TokenId = tokenId,
			Summary = summary
		};

		for (var index = 0; index < changes.Count; index++)
		{
			var change = Resolve(changes[index], created);
			var outcome = await ApplyOne(change);
			if (outcome.Problem is { } problem)
			{
				await Revert(batch.Items);
				return new McpChangeResult(false, null, [problem with { Index = index }], created);
			}

			if (outcome.Item is { } item)
			{
				batch.Items.Add(item);
			}

			if (change is { Op: McpChangeOps.Create, Ref: { Length: > 0 } reference } && outcome.CreatedId is { } id)
			{
				created[reference] = id.ToString();
			}
		}

		foreach (var item in batch.Items)
		{
			item.AfterFingerprint = await _entities.CurrentFingerprint(item.Entity, item.Id);
			item.StartFoldersAfter = _entities.StartFoldersAfter(item);
		}

		_secrets.Defer(_deferral.Take());
		await _journal.Append(batch);

		return new McpChangeResult(true, batch.BatchId, [], created);
	}

	private async Task<IReadOnlyList<McpProblem>> UndoLocked(Guid batchId)
	{
		_authority.Use(SecretApprovalWrite.Withhold);
		_deferral.Activate();

		var batch = _journal.Find(batchId);
		if (batch is null || batch.Undone)
		{
			return [new McpProblem(0, "batchId", McpProblemCodes.NotFound, "No undoable batch with that id.")];
		}

		var problems = new List<McpProblem>();
		for (var index = batch.Items.Count - 1; index >= 0; index--)
		{
			var item = batch.Items[index];
			var current = await _entities.CurrentFingerprint(item.Entity, item.Id);
			var unchanged = item.Operation == McpJournalOperation.Deleted
				? current is null
				: current == item.AfterFingerprint;
			if (!unchanged)
			{
				problems.Add(new McpProblem(index,
					item.Entity,
					McpProblemCodes.EntityChangedSince,
					$"The {item.Entity} {item.Id} changed after this batch and was left as it is."));
				continue;
			}

			var undone = item.Operation == McpJournalOperation.Created
				? await DeleteCreated(item) ? McpRestoreResult.Restored : McpRestoreResult.Failed
				: await _entities.Restore(item);
			if (undone == McpRestoreResult.StartFolderKept)
			{
				problems.Add(new McpProblem(index,
					item.Entity,
					McpProblemCodes.EntityChangedSince,
					$"The start folder changed after this batch and was left as it is; folder {item.Id} is back."));
			}
			else if (undone == McpRestoreResult.Failed)
			{
				problems.Add(new McpProblem(index,
					item.Entity,
					McpProblemCodes.ApplyFailed,
					$"The {item.Entity} {item.Id} could not be restored."));
			}
		}

		_secrets.Defer(_deferral.Take());
		_journal.MarkUndone(batchId);
		return problems;
	}

	private async Task ValidateOne(McpChange change, ValidationContext context)
	{
		var data = change.Data;
		var op = change.Op?.Trim().ToLowerInvariant();
		if (op is not (McpChangeOps.Create or McpChangeOps.Update or McpChangeOps.Delete))
		{
			context.Problem("op", McpProblemCodes.InvalidArgument, "op must be create, update or delete.");
			return;
		}

		if (op != McpChangeOps.Create && !TryId(change.Id, context, out _))
		{
			return;
		}

		switch (change.Entity?.Trim().ToLowerInvariant())
		{
			case McpEntities.Widget:
				await ValidateWidget(op, change, data, context);
				break;
			case McpEntities.Folder:
				ValidateFolder(op, change, data, context);
				break;
			case McpEntities.Profile:
				ValidateProfile(op, change, data, context);
				break;
			case McpEntities.Automation:
				await ValidateFlowOwner(op,
					change,
					data,
					context,
					id => _automations.GetById(id)?.Flows,
					FlowOwner.Automation);
				break;
			case McpEntities.Script:
				await ValidateFlowOwner(op, change, data, context, id => _scripts.GetById(id)?.Flows, FlowOwner.Script);
				ValidateScriptInputs(data, context);
				break;
			case McpEntities.Variable:
				await ValidateVariable(op, change, data, context);
				break;
			default:
				context.Problem("entity",
					McpProblemCodes.InvalidArgument,
					"entity must be profile, folder, widget, automation, script or variable.");
				break;
		}
	}

	private async Task ValidateWidget(string op, McpChange change, JsonElement? data, ValidationContext context)
	{
		WidgetEntity? current = null;
		if (op != McpChangeOps.Create)
		{
			current = FindWidget(Guid.Parse(change.Id!));
			if (current is null)
			{
				context.Problem("id", McpProblemCodes.NotFound, "No widget with that id.");
				return;
			}

			if (op == McpChangeOps.Delete)
			{
				return;
			}

			if (Str(data, "folderId") is { } folderId && folderId != current.FolderId.ToString())
			{
				context.Problem("data.folderId",
					McpProblemCodes.Unsupported,
					"Moving a widget to another folder is not supported.");
			}
		}
		else if (!Exists(Str(data, "folderId"),
			McpEntities.Folder,
			context,
			id => _folders.GetFolderById(id) is not null))
		{
			context.Problem("data.folderId", McpProblemCodes.NotFound, "folderId must name an existing folder.");
		}

		var type = Str(data, "type") ?? current?.Type ?? WidgetTypeIds.ActionButton;
		var widgetData = Json(data, "data") ?? current?.Data;
		if (widgetData is not null)
		{
			ValidateWidgetData(type, widgetData, context);
		}

		var owner = current is null ? (FlowOwner?)null : FlowOwner.Widget(current.Id);
		var flows = WidgetFlowsJson.TryExtract(widgetData, out var extracted) ? extracted : null;
		var storedFlows = current is not null && WidgetFlowsJson.TryExtract(current.Data, out var stored)
			? stored
			: null;
		await ValidateFlows(flows, storedFlows, owner, "data.data", context);
		ValidateEventTriggers(EventTriggerParser.Parse(new EventTriggerOwner(EventTriggerOwnerKind.Widget, Guid.Empty),
				widgetData),
			context);
		ValidateOutsideBlockReferences(widgetData, current?.Data, flows, context);
	}

	private void ValidateWidgetData(string type, string widgetData, ValidationContext context)
	{
		try
		{
			using var document = JsonDocument.Parse(widgetData);
			if (document.RootElement.ValueKind != JsonValueKind.Object)
			{
				context.Problem("data.data", McpProblemCodes.InvalidData, "data must be a JSON object.");
				return;
			}

			if (_schemas.TryGet(type, out var schema))
			{
				foreach (var problem in WidgetDataSchema.Validate(schema, document.RootElement))
				{
					context.Problem("data.data" + problem.Pointer, McpProblemCodes.InvalidData, problem.Message);
				}
			}
			else if (!_schemas.All().ContainsKey(type) && type != WidgetTypeIds.ActionButton)
			{
				context.Problem("data.type", McpProblemCodes.WidgetTypeNotFound, $"Unknown widget type '{type}'.");
			}
		}
		catch (JsonException)
		{
			context.Problem("data.data", McpProblemCodes.InvalidData, "data is not valid JSON.");
		}
	}

	private void ValidateFolder(string op, McpChange change, JsonElement? data, ValidationContext context)
	{
		if (op == McpChangeOps.Create)
		{
			if (!Exists(Str(data, "profileId"), McpEntities.Profile, context, id => _profiles.GetById(id) is not null))
			{
				context.Problem("data.profileId", McpProblemCodes.NotFound, "profileId must name an existing profile.");
			}

			if (string.IsNullOrWhiteSpace(Str(data, "name")))
			{
				context.Problem("data.name", McpProblemCodes.InvalidArgument, "name is required.");
			}

			return;
		}

		if (_folders.GetFolderById(Guid.Parse(change.Id!)) is null)
		{
			context.Problem("id", McpProblemCodes.NotFound, "No folder with that id.");
		}
	}

	private void ValidateProfile(string op, McpChange change, JsonElement? data, ValidationContext context)
	{
		if (op == McpChangeOps.Delete)
		{
			context.Problem("op", McpProblemCodes.Unsupported, "Profiles cannot be deleted through MCP.");
			return;
		}

		if (op == McpChangeOps.Create && string.IsNullOrWhiteSpace(Str(data, "name")))
		{
			context.Problem("data.name", McpProblemCodes.InvalidArgument, "name is required.");
		}

		if (op == McpChangeOps.Update && _profiles.GetById(Guid.Parse(change.Id!)) is null)
		{
			context.Problem("id", McpProblemCodes.NotFound, "No profile with that id.");
		}
	}

	private async Task ValidateFlowOwner(
		string op,
		McpChange change,
		JsonElement? data,
		ValidationContext context,
		Func<Guid, string?> storedFlows,
		Func<Guid, FlowOwner> owner)
	{
		Guid? id = op == McpChangeOps.Create ? null : Guid.Parse(change.Id!);
		var stored = id is { } existing ? storedFlows(existing) : null;
		if (id is not null && stored is null)
		{
			context.Problem("id", McpProblemCodes.NotFound, $"No {change.Entity} with that id.");
			return;
		}

		if (op == McpChangeOps.Delete)
		{
			return;
		}

		if (op == McpChangeOps.Create && string.IsNullOrWhiteSpace(Str(data, "name")))
		{
			context.Problem("data.name", McpProblemCodes.InvalidArgument, "name is required.");
		}

		var flows = Json(data, "flows");
		if (flows is null)
		{
			return;
		}

		try
		{
			using var document = JsonDocument.Parse(flows);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				context.Problem("data.flows", McpProblemCodes.InvalidData, "flows must be a JSON array.");
				return;
			}
		}
		catch (JsonException)
		{
			context.Problem("data.flows", McpProblemCodes.InvalidData, "flows is not valid JSON.");
			return;
		}

		await ValidateFlows(flows, stored, id is { } known ? owner(known) : null, "data.flows", context);
		ValidateEventTriggers(EventTriggerParser.ParseFlows(
				new EventTriggerOwner(EventTriggerOwnerKind.Automation, Guid.Empty),
				flows),
			context);
		ValidateOutsideBlockReferences(flows, stored, flows, context);
	}

	private static void ValidateScriptInputs(JsonElement? data, ValidationContext context)
	{
		if (data is not { } value || !value.TryGetProperty("inputs", out var inputs))
		{
			return;
		}

		try
		{
			var parsed = inputs.Deserialize<List<ScriptInput>>(McpJsonOptions.Default) ?? [];
			if (ScriptService.ValidateInputs(parsed) is { } failure)
			{
				context.Problem("data.inputs", McpProblemCodes.InvalidData, failure.ErrorMessage ?? "Invalid inputs.");
			}
		}
		catch (JsonException)
		{
			context.Problem("data.inputs", McpProblemCodes.InvalidData, "inputs must be a list of input definitions.");
		}
	}

	private async Task ValidateVariable(string op, McpChange change, JsonElement? data, ValidationContext context)
	{
		VariableEntity? current = null;
		if (op != McpChangeOps.Create)
		{
			current = await _variables.GetById(Guid.Parse(change.Id!));
			if (current is null)
			{
				context.Problem("id", McpProblemCodes.NotFound, "No variable with that id.");
				return;
			}
		}

		if (data is { ValueKind: JsonValueKind.Object } payload
			&& payload.TryGetProperty("value", out var given)
			&& given.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
		{
			context.Problem("data.value",
				McpProblemCodes.InvalidArgument,
				"value must be a string, a number or a boolean.");
		}

		var name = op == McpChangeOps.Create ? Str(data, "name") : current!.Name;
		if (op == McpChangeOps.Create)
		{
			if (name is null || !VariableNameSanitizer.IsValid(name))
			{
				context.Problem("data.name", McpProblemCodes.InvalidArgument, "name must match ^[a-z][a-z0-9_]*$.");
			}

			if (Str(data, "scope") is { } scope && scope is not ("global" or "script" or "automation"))
			{
				context.Problem("data.scope",
					McpProblemCodes.Unsupported,
					"scope must be global, script or automation.");
			}
		}

		if (name is not null && context.ProtectedVariables.Contains(name))
		{
			context.Confirm(McpConfirmationKinds.ProtectedVariable,
				$"Variable '{name}' is read by an approved block that uses a secret.",
				name: name);
		}
	}

	private async Task ValidateFlows(
		string? flows,
		string? storedFlows,
		FlowOwner? owner,
		string path,
		ValidationContext context)
	{
		var stored = FlowSecretBlocks.ActionBlocksOfFlows(storedFlows)
			.Where(block => !string.IsNullOrEmpty(block.Id))
			.GroupBy(block => block.Id, StringComparer.Ordinal)
			.ToDictionary(group => group.Key,
				group => group.Select(block => _approvals.Fingerprint(block.Content)).ToHashSet(StringComparer.Ordinal),
				StringComparer.Ordinal);

		foreach (var block in FlowSecretBlocks.ActionBlocksOfFlows(flows))
		{
			var unchanged = owner is not null
				&& stored.TryGetValue(block.Id, out var fingerprints)
				&& fingerprints.Contains(_approvals.Fingerprint(block.Content));
			if (unchanged)
			{
				continue;
			}

			if (block.Content is not { IntegrationId: { } integrationId, ActionId: { } actionId }
				|| _integrations.FindAction(integrationId, actionId) is not { } action)
			{
				context.Problem($"{path} block {block.Id}",
					McpProblemCodes.ActionNotFound,
					$"Unknown action {block.Content.IntegrationId}.{block.Content.ActionId}.");
				continue;
			}

			foreach (var required in action.Parameters.Where(parameter => parameter.Required))
			{
				if (block.Content.Parameters.All(parameter => parameter.Name != required.Name))
				{
					context.Problem($"{path} block {block.Id}",
						McpProblemCodes.ParameterRequired,
						$"Parameter '{required.Name}' of {integrationId}.{actionId} is required.");
				}
			}

			switch (await _risk.Classify(integrationId, actionId))
			{
				case McpRiskMode.Off:
					context.Problem($"{path} block {block.Id}",
						McpProblemCodes.ProviderBlocked,
						$"{integrationId}.{actionId} is turned off for AI clients.");
					break;
				case McpRiskMode.Confirm:
					context.Confirm(McpConfirmationKinds.RiskyAction,
						$"{integrationId}.{actionId}",
						integrationId,
						actionId);
					break;
			}

			ConfirmSteering(block.Content, context);

			foreach (var secretId in SecretReferences.Extract(block.Raw))
			{
				if (!_secrets.IsGranted(context.TokenId, secretId))
				{
					context.Problem($"{path} block {block.Id}",
						McpProblemCodes.ForeignSecretRef,
						$"Secret {secretId} was not created by this client with set_secret.");
				}
			}
		}
	}

	private void ConfirmSteering(FlowBlockContent block, ValidationContext context)
	{
		foreach (var parameter in
			block.Parameters.Where(parameter => parameter.Value.ValueKind == JsonValueKind.String))
		{
			var value = parameter.Value.GetString()!;
			if (context.ProtectedVariables.Contains(value))
			{
				context.Confirm(McpConfirmationKinds.ProtectedVariable,
					$"Variable '{value}' is read by an approved block that uses a secret.",
					name: value);
			}
		}

		if (McpRunInspector.ScriptTarget(block) is not { } script)
		{
			return;
		}

		var steering = _inspector.SteeringScriptInputs(script);
		foreach (var input in block.Parameters
			.Where(parameter => parameter.Name.StartsWith(McpRunInspector.InputPrefix, StringComparison.Ordinal))
			.Select(parameter => parameter.Name[McpRunInspector.InputPrefix.Length..])
			.Where(steering.Contains))
		{
			context.Confirm(McpConfirmationKinds.SteeringValue,
				$"Input '{input}' reaches an approved block that uses a secret.",
				name: input);
		}
	}

	private void ValidateOutsideBlockReferences(string? text, string? stored, string? flows, ValidationContext context)
	{
		var inBlocks = FlowSecretBlocks.ActionBlocksOfFlows(flows)
			.SelectMany(block => SecretReferences.Extract(block.Raw))
			.ToHashSet();
		var allowed = SecretReferences.Extract(stored).ToHashSet();
		foreach (var secretId in SecretReferences.Extract(text).Where(id => !inBlocks.Contains(id)))
		{
			if (!allowed.Contains(secretId) && !_secrets.IsGranted(context.TokenId, secretId))
			{
				context.Problem("data",
					McpProblemCodes.ForeignSecretRef,
					$"Secret {secretId} was not created by this client with set_secret.");
			}
		}
	}

	private void ValidateEventTriggers(IEnumerable<EventSubscription> subscriptions, ValidationContext context)
	{
		foreach (var subscription in subscriptions.Where(subscription => _events.Find(subscription.EventId) is null))
		{
			context.Problem("data.flows",
				McpProblemCodes.EventNotFound,
				$"Unknown event '{subscription.EventId}'.");
		}
	}

	private async Task<Outcome> ApplyOne(McpChange change)
	{
		var entity = change.Entity.Trim().ToLowerInvariant();
		var op = change.Op.Trim().ToLowerInvariant();
		var data = change.Data;

		if (op != McpChangeOps.Create)
		{
			var id = Guid.Parse(change.Id!);
			var item = await _entities.Capture(entity,
				id,
				op == McpChangeOps.Delete ? McpJournalOperation.Deleted : McpJournalOperation.Updated);
			var error = op == McpChangeOps.Delete ? await Delete(entity, id, data) : await Update(entity, id, data);
			return error is null ? new Outcome(item, null, null) : Failed(error);
		}

		var (createdId, createError) = await Create(entity, data);
		if (createError is not null || createdId is not { } created)
		{
			return Failed(createError);
		}

		return new Outcome(
			new McpJournalItem { Entity = entity, Id = created, Operation = McpJournalOperation.Created },
			created,
			null);
	}

	private async Task<(Guid? Id, TransportError? Error)> Create(string entity, JsonElement? data)
	{
		switch (entity)
		{
			case McpEntities.Widget:
			{
				var response = await Handle<CreateWidgetRequest, CreateWidgetResponse>(new CreateWidgetRequest
				{
					FolderId = Str(data, "folderId") ?? string.Empty,
					Type = Str(data, "type") ?? WidgetTypeIds.ActionButton,
					PositionX = Int(data, "positionX") ?? 0,
					PositionY = Int(data, "positionY") ?? 0,
					Width = Int(data, "width") ?? 1,
					Height = Int(data, "height") ?? 1,
					Data = Json(data, "data")
				});
				return (response.Success ? ParseId(response.Widget?.Id) : null,
					response.Success ? null : response.Error);
			}
			case McpEntities.Folder:
			{
				var response = await Handle<CreateFolderRequest, CreateFolderResponse>(new CreateFolderRequest
				{
					ProfileId = Str(data, "profileId") ?? string.Empty,
					Name = Str(data, "name") ?? string.Empty,
					ParentId = Str(data, "parentId")
				});
				return (response.Success ? ParseId(response.Folder?.Id) : null,
					response.Success ? null : response.Error);
			}
			case McpEntities.Profile:
			{
				var response = await Handle<CreateProfileRequest, CreateProfileResponse>(new CreateProfileRequest
				{
					Name = Str(data, "name") ?? string.Empty,
					DefaultRows = Int(data, "defaultRows"),
					DefaultColumns = Int(data, "defaultColumns")
				});
				return (response.Success ? ParseId(response.Profile?.Id) : null,
					response.Success ? null : response.Error);
			}
			case McpEntities.Automation:
			{
				var response = await Handle<CreateAutomationRequest, CreateAutomationResponse>(
					new CreateAutomationRequest
					{
						Name = Str(data, "name") ?? string.Empty,
						Description = Str(data, "description"),
						Flows = Json(data, "flows")
					});
				if (response.Success && Bool(data, "enabled") == false && ParseId(response.Automation?.Id) is { } id)
				{
					await Handle<UpdateAutomationRequest, UpdateAutomationResponse>(new UpdateAutomationRequest
					{
						Id = id.ToString(),
						Enabled = false
					});
				}

				return (response.Success ? ParseId(response.Automation?.Id) : null,
					response.Success ? null : response.Error);
			}
			case McpEntities.Script:
			{
				var response = await Handle<CreateScriptRequest, CreateScriptResponse>(new CreateScriptRequest
				{
					Name = Str(data, "name") ?? string.Empty,
					Description = Str(data, "description"),
					Flows = Json(data, "flows"),
					Inputs = Inputs(data),
					RunsOnWidget = Bool(data, "runsOnWidget") ?? false
				});
				return (response.Success ? ParseId(response.Script?.Id) : null,
					response.Success ? null : response.Error);
			}
			default:
			{
				var response = await Handle<CreateVariableRequest, CreateVariableResponse>(new CreateVariableRequest
				{
					Name = Str(data, "name") ?? string.Empty,
					Scope = Str(data, "scope") ?? "global",
					ScopeRefId = Str(data, "scopeRefId"),
					Type = Str(data, "type") ?? "text",
					InitialValue = ValueText(data)
				});
				return (response.Success ? ParseId(response.Variable?.Id) : null,
					response.Success ? null : response.Error);
			}
		}
	}

	private async Task<TransportError?> Update(string entity, Guid id, JsonElement? data)
	{
		switch (entity)
		{
			case McpEntities.Widget:
			{
				var current = FindWidget(id)!;
				var response = await Handle<UpdateWidgetRequest, UpdateWidgetResponse>(new UpdateWidgetRequest
				{
					Id = id.ToString(),
					FolderId = current.FolderId.ToString(),
					Type = Str(data, "type") ?? current.Type,
					PositionX = Int(data, "positionX") ?? current.PositionX,
					PositionY = Int(data, "positionY") ?? current.PositionY,
					Width = Int(data, "width") ?? current.Width,
					Height = Int(data, "height") ?? current.Height,
					Data = Json(data, "data") ?? current.Data
				});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Folder:
			{
				var response = await Handle<UpdateFolderRequest, UpdateFolderResponse>(new UpdateFolderRequest
				{
					Id = id.ToString(),
					Name = Str(data, "name"),
					Rows = Int(data, "rows"),
					Columns = Int(data, "columns")
				});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Profile:
			{
				var response = await Handle<UpdateProfileRequest, UpdateProfileResponse>(new UpdateProfileRequest
				{
					Id = id.ToString(),
					Name = Str(data, "name"),
					DefaultRows = Int(data, "defaultRows"),
					DefaultColumns = Int(data, "defaultColumns")
				});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Automation:
			{
				var response = await Handle<UpdateAutomationRequest, UpdateAutomationResponse>(
					new UpdateAutomationRequest
					{
						Id = id.ToString(),
						Name = Str(data, "name"),
						Description = Str(data, "description"),
						Flows = Json(data, "flows"),
						Enabled = Bool(data, "enabled")
					});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Script:
			{
				var response = await Handle<UpdateScriptRequest, UpdateScriptResponse>(new UpdateScriptRequest
				{
					Id = id.ToString(),
					Name = Str(data, "name"),
					Description = Str(data, "description"),
					Flows = Json(data, "flows"),
					Inputs = Inputs(data),
					RunsOnWidget = Bool(data, "runsOnWidget")
				});
				return response.Success ? null : response.Error;
			}
			default:
			{
				var response = await Handle<SetVariableValueRequest, SetVariableValueResponse>(
					new SetVariableValueRequest
					{
						Id = id.ToString(),
						Value = ValueText(data)
					});
				return response.Success ? null : response.Error;
			}
		}
	}

	private async Task<TransportError?> Delete(string entity, Guid id, JsonElement? data)
	{
		_ = data;
		switch (entity)
		{
			case McpEntities.Widget:
			{
				var folderId = FindWidget(id)?.FolderId ?? Guid.Empty;
				var response = await Handle<DeleteWidgetRequest, DeleteWidgetResponse>(new DeleteWidgetRequest
				{
					Id = id.ToString(),
					FolderId = folderId.ToString()
				});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Folder:
			{
				var response = await Handle<DeleteFolderRequest, DeleteFolderResponse>(new DeleteFolderRequest
				{
					Id = id.ToString()
				});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Automation:
			{
				var response = await Handle<DeleteAutomationRequest, DeleteAutomationResponse>(
					new DeleteAutomationRequest
					{
						Id = id.ToString()
					});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Script:
			{
				var response = await Handle<DeleteScriptRequest, DeleteScriptResponse>(new DeleteScriptRequest
				{
					Id = id.ToString()
				});
				return response.Success ? null : response.Error;
			}
			case McpEntities.Variable:
			{
				var response = await Handle<DeleteVariableRequest, DeleteVariableResponse>(new DeleteVariableRequest
				{
					Id = id.ToString()
				});
				return response.Success ? null : response.Error;
			}
			default:
				return new TransportError { Code = McpProblemCodes.Unsupported, Message = "Unsupported delete." };
		}
	}

	private async Task<bool> DeleteCreated(McpJournalItem item)
	{
		if (item.Entity == McpEntities.Profile)
		{
			var profiles = _services.GetRequiredService<IProfileService>();
			return (await profiles.Delete(item.Id)).Success;
		}

		return await Delete(item.Entity, item.Id, null) is null;
	}

	private async Task Revert(List<McpJournalItem> applied)
	{
		for (var index = applied.Count - 1; index >= 0; index--)
		{
			var item = applied[index];
			if (item.Operation == McpJournalOperation.Created)
			{
				await DeleteCreated(item);
			}
			else
			{
				item.StartFoldersAfter = _entities.StartFoldersAfter(item);
				await _entities.Restore(item);
			}
		}

		_secrets.Defer(_deferral.Take());
	}

	private Outcome Failed(TransportError? error)
	{
		var message = error is null
			? "The change could not be applied."
			: _localization.Resolve(error.Message, LocalizationDefaults.Culture) ?? error.Code;
		return new Outcome(null,
			null,
			new McpProblem(0, string.Empty, error?.Code ?? McpProblemCodes.ApplyFailed, message));
	}

	private async Task<TResponse> Handle<TRequest, TResponse>(TRequest request)
		where TRequest : class
		where TResponse : class
	{
		return await _services.GetRequiredService<IUiTransportMessageHandler<TRequest, TResponse>>()
			.Handle(request, CancellationToken.None);
	}

	private WidgetEntity? FindWidget(Guid id)
	{
		return _folders.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(widget => widget.Id == id);
	}

	private static McpChange Resolve(McpChange change, Dictionary<string, string> created)
	{
		var id = change.Id is { } raw
			&& raw.StartsWith(RefPrefix, StringComparison.Ordinal)
			&& created.TryGetValue(raw[RefPrefix.Length..], out var mapped)
				? mapped
				: change.Id;
		if (change.Data is not { } data)
		{
			return change with { Id = id };
		}

		var node = JsonNode.Parse(data.GetRawText());
		var replaced = ReplaceRefs(node, created);
		return change with { Id = id, Data = JsonSerializer.SerializeToElement(replaced) };
	}

	private static JsonNode? ReplaceRefs(JsonNode? node, Dictionary<string, string> created)
	{
		switch (node)
		{
			case JsonValue value when value.TryGetValue<string>(out var text)
				&& text.StartsWith(RefPrefix, StringComparison.Ordinal)
				&& created.TryGetValue(text[RefPrefix.Length..], out var id):
				return JsonValue.Create(id);
			case JsonObject obj:
				foreach (var key in obj.Select(property => property.Key).ToList())
				{
					obj[key] = ReplaceRefs(obj[key]?.DeepClone(), created);
				}

				return obj;
			case JsonArray array:
				for (var index = 0; index < array.Count; index++)
				{
					array[index] = ReplaceRefs(array[index]?.DeepClone(), created);
				}

				return array;
			default:
				return node;
		}
	}

	private static bool TryId(string? id, ValidationContext context, out Guid value)
	{
		if (id is { } raw && raw.StartsWith(RefPrefix, StringComparison.Ordinal))
		{
			value = Guid.Empty;
			if (!context.Declared.ContainsKey(raw[RefPrefix.Length..]))
			{
				context.Problem("id", McpProblemCodes.RefUnresolved, $"'{raw}' does not name an earlier create.");
			}
			else
			{
				context.Problem("id",
					McpProblemCodes.Unsupported,
					"Changing an object created in the same batch is not supported.");
			}

			return false;
		}

		if (!Guid.TryParse(id, out value))
		{
			context.Problem("id",
				McpProblemCodes.InvalidArgument,
				"id is required and must be an id from list or get.");
			return false;
		}

		return true;
	}

	private static bool Exists(string? id, string entity, ValidationContext context, Func<Guid, bool> lookup)
	{
		if (id is { } raw && raw.StartsWith(RefPrefix, StringComparison.Ordinal))
		{
			if (context.Declared.TryGetValue(raw[RefPrefix.Length..], out var declared) && declared == entity)
			{
				return true;
			}

			context.Problem("data",
				McpProblemCodes.RefUnresolved,
				$"'{raw}' does not name an earlier {entity} create.");
			return true;
		}

		return Guid.TryParse(id, out var value) && lookup(value);
	}

	private static string? Str(JsonElement? data, string name)
	{
		return data is { ValueKind: JsonValueKind.Object } value
			&& value.TryGetProperty(name, out var property)
			&& property.ValueKind == JsonValueKind.String
				? property.GetString()
				: null;
	}

	private static string? ValueText(JsonElement? data)
	{
		if (data is not { ValueKind: JsonValueKind.Object } value || !value.TryGetProperty("value", out var property))
		{
			return null;
		}

		return property.ValueKind switch
		{
			JsonValueKind.String => property.GetString(),
			JsonValueKind.Number => property.GetRawText(),
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			_ => null
		};
	}

	private static int? Int(JsonElement? data, string name)
	{
		return data is { ValueKind: JsonValueKind.Object } value
			&& value.TryGetProperty(name, out var property)
			&& property.ValueKind == JsonValueKind.Number
			&& property.TryGetInt32(out var number)
				? number
				: null;
	}

	private static bool? Bool(JsonElement? data, string name)
	{
		return data is { ValueKind: JsonValueKind.Object } value
			&& value.TryGetProperty(name, out var property)
			&& property.ValueKind is JsonValueKind.True or JsonValueKind.False
				? property.GetBoolean()
				: null;
	}

	private static string? Json(JsonElement? data, string name)
	{
		return data is { ValueKind: JsonValueKind.Object } value
			&& value.TryGetProperty(name, out var property)
			&& property.ValueKind is JsonValueKind.Object or JsonValueKind.Array
				? property.GetRawText()
				: null;
	}

	private static List<ScriptInput>? Inputs(JsonElement? data)
	{
		return data is { ValueKind: JsonValueKind.Object } value && value.TryGetProperty("inputs", out var inputs)
			? inputs.Deserialize<List<ScriptInput>>(McpJsonOptions.Default)
			: null;
	}

	private static Guid? ParseId(string? id)
	{
		return Guid.TryParse(id, out var value) ? value : null;
	}

	private sealed record Outcome(McpJournalItem? Item, Guid? CreatedId, McpProblem? Problem);

	private sealed record ValidationContext(
		int Index,
		Guid TokenId,
		List<McpProblem> Problems,
		List<McpConfirmation> Confirmations,
		Dictionary<string, string> Declared,
		IReadOnlySet<string> ProtectedVariables)
	{
		public void Problem(string path, string code, string message)
		{
			Problems.Add(new McpProblem(Index, path, code, message));
		}

		public void Confirm(
			string kind,
			string detail,
			string? integrationId = null,
			string? actionId = null,
			string? name = null)
		{
			if (!Confirmations.Any(existing => existing.Kind == kind && existing.Detail == detail))
			{
				Confirmations.Add(new McpConfirmation(Index, kind, detail, integrationId, actionId, name));
			}
		}
	}
}

public static class McpJsonOptions
{
	public static readonly JsonSerializerOptions Default = new(JsonSerializerDefaults.Web);
}
