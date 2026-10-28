using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Secrets.Approvals;

public sealed class PendingSecretApprovalParameter
{
	public required string Name { get; init; }

	public LocalizedText? Label { get; init; }

	public bool Secret { get; init; }

	public string? Approved { get; init; }

	public string? Current { get; init; }

	public bool Changed { get; init; }

	public IReadOnlyList<string> AlsoUsedBy { get; init; } = [];
}

public sealed class PendingSecretApproval
{
	public required string OwnerKind { get; init; }

	public required string OwnerId { get; init; }

	public required string OwnerName { get; init; }

	public string? OwnerContext { get; init; }

	public required string BlockId { get; init; }

	public required string IntegrationId { get; init; }

	public required string ActionId { get; init; }

	public LocalizedText ActionName { get; init; }

	public required string Fingerprint { get; init; }

	public bool ApprovedBefore { get; init; }

	public bool Unknown { get; init; }

	public IReadOnlyList<PendingSecretApprovalParameter> Parameters { get; init; } = [];
}

public sealed class PendingSecretApprovalsResponse
{
	public int Count { get; init; }

	public IReadOnlyList<PendingSecretApproval> Items { get; init; } = [];
}

public sealed class ApproveSecretBlockRequest
{
	public string OwnerKind { get; set; } = string.Empty;

	public string OwnerId { get; set; } = string.Empty;

	public string BlockId { get; set; } = string.Empty;

	public string Fingerprint { get; set; } = string.Empty;
}

public sealed class ApproveSecretBlockResponse
{
	public const string ChangedCode = "APPROVAL_CHANGED";
	public const string NotFoundCode = "APPROVAL_NOT_FOUND";

	public bool Success { get; init; }

	public TransportError? Error { get; init; }
}

public sealed class SecretApprovalCatalog
{
	private readonly IFolderCache _folders;
	private readonly IProfileCache _profiles;
	private readonly IAutomationCache _automations;
	private readonly IScriptCache _scripts;
	private readonly IIntegrationRegistry _registry;
	private readonly ISecretApprovals _approvals;
	private readonly ISecretLineage? _lineage;

	public SecretApprovalCatalog(
		IFolderCache folders,
		IProfileCache profiles,
		IAutomationCache automations,
		IScriptCache scripts,
		IIntegrationRegistry registry,
		ISecretApprovals approvals,
		ISecretLineage? lineage = null)
	{
		_folders = folders;
		_profiles = profiles;
		_automations = automations;
		_scripts = scripts;
		_registry = registry;
		_approvals = approvals;
		_lineage = lineage;
	}

	public PendingSecretApprovalsResponse Pending()
	{
		var owners = Owners();
		var usage = SecretUsage(owners);
		var items = new List<PendingSecretApproval>();

		foreach (var owner in owners)
		{
			foreach (var block in owner.Blocks)
			{
				if (block.Content is not { IntegrationId: { } integrationId, ActionId: { } actionId }
					|| _registry.FindAction(integrationId, actionId) is not { } action
					|| !HasDeclaredSecret(action, block.Content)
					|| block.Content.HasDuplicateParameterNames
					|| _approvals.IsApproved(owner.Owner, block.Id, block.Content))
				{
					continue;
				}

				var approvedSnapshot = _approvals.ApprovedSnapshot(owner.Owner, block.Id) is { } raw
					? FlowSecretBlocks.Parse(raw)
					: null;
				items.Add(new PendingSecretApproval
				{
					OwnerKind = KindName(owner.Owner.Kind),
					OwnerId = owner.Owner.Id.ToString(),
					OwnerName = owner.Name,
					OwnerContext = owner.Context,
					BlockId = block.Id,
					IntegrationId = integrationId,
					ActionId = actionId,
					ActionName = action.Name,
					Fingerprint = _approvals.Fingerprint(block.Content),
					ApprovedBefore = approvedSnapshot is not null,
					Unknown = _approvals.IsUnknown(owner.Owner, block.Id, block.Content),
					Parameters = Describe(action, block.Content, approvedSnapshot?.Content, owner, usage)
				});
			}
		}

		return new PendingSecretApprovalsResponse { Count = items.Count, Items = items };
	}

	public ApproveSecretBlockResponse Approve(ApproveSecretBlockRequest request)
	{
		var target = Owners()
			.FirstOrDefault(owner
				=> string.Equals(KindName(owner.Owner.Kind), request.OwnerKind, StringComparison.Ordinal)
				&& string.Equals(owner.Owner.Id.ToString(), request.OwnerId, StringComparison.OrdinalIgnoreCase));
		var candidates = target?.Blocks
				.Where(block => !string.IsNullOrEmpty(block.Id)
					&& string.Equals(block.Id, request.BlockId, StringComparison.Ordinal))
				.ToList()
			?? [];

		if (target is null || candidates.Count == 0)
		{
			return Fail(ApproveSecretBlockResponse.NotFoundCode, AppStrings.Errors.Secrets.ApprovalNotFound());
		}

		return candidates.Any(block => _approvals.Approve(target.Owner, block, request.Fingerprint))
			? new ApproveSecretBlockResponse { Success = true }
			: Fail(ApproveSecretBlockResponse.ChangedCode, AppStrings.Errors.Secrets.ApprovalChanged());
	}

	private static ApproveSecretBlockResponse Fail(string code, LocalizedText message)
	{
		return new ApproveSecretBlockResponse { Error = new TransportError { Code = code, Message = message } };
	}

	private List<OwnerBlocks> Owners()
	{
		var owners = new List<OwnerBlocks>();
		foreach (var folder in _folders.GetAllFolders())
		{
			var profileName = _profiles.GetById(folder.ProfileId)?.Name;
			foreach (var widget in folder.Widgets)
			{
				var blocks = FlowSecretBlocks.FromWidgetData(widget.Data);
				if (blocks.Count > 0)
				{
					owners.Add(new OwnerBlocks(FlowOwner.Widget(widget.Id), folder.Name, profileName, blocks));
				}
			}
		}

		foreach (var automation in _automations.GetAll())
		{
			var blocks = FlowSecretBlocks.FromFlows(automation.Flows);
			if (blocks.Count > 0)
			{
				owners.Add(new OwnerBlocks(FlowOwner.Automation(automation.Id), automation.Name, null, blocks));
			}
		}

		foreach (var script in _scripts.GetAll())
		{
			var blocks = FlowSecretBlocks.FromFlows(script.Flows);
			if (blocks.Count > 0)
			{
				owners.Add(new OwnerBlocks(FlowOwner.Script(script.Id), script.Name, null, blocks));
			}
		}

		return owners;
	}

	private Dictionary<Guid, HashSet<OwnerBlocks>> SecretUsage(List<OwnerBlocks> owners)
	{
		var usage = new Dictionary<Guid, HashSet<OwnerBlocks>>();
		foreach (var owner in owners)
		{
			foreach (var parameter in owner.Blocks.SelectMany(block => block.Content.Parameters))
			{
				foreach (var id in ReferencedIds(parameter.Value))
				{
					if (!usage.TryGetValue(id, out var users))
					{
						users = usage[id] = [];
					}

					users.Add(owner);
				}
			}
		}

		return usage;
	}

	private List<PendingSecretApprovalParameter> Describe(
		IActionDefinition action,
		FlowBlockContent current,
		FlowBlockContent? approved,
		OwnerBlocks owner,
		Dictionary<Guid, HashSet<OwnerBlocks>> usage)
	{
		var declared = DeclaredSecretWalker.Index(action.Parameters);
		var names = action.Parameters.Select(parameter => parameter.Name)
			.Concat(current.Parameters.Select(parameter => parameter.Name))
			.Distinct(StringComparer.Ordinal);
		var result = new List<PendingSecretApprovalParameter>();

		foreach (var name in names)
		{
			declared.TryGetValue(name, out var definition);
			var now = ValueOf(current, name, definition);
			var before = approved is null ? (JsonElement?)null : ValueOf(approved, name, definition);
			var secret = definition is not null
				&& (DeclaredSecretWalker.IsSecret(definition)
					|| DeclaredSecretWalker.SecretIds(definition, now).Count > 0);

			result.Add(new PendingSecretApprovalParameter
			{
				Name = name,
				Label = definition is null || definition.Label.IsEmpty ? null : definition.Label,
				Secret = secret,
				Approved = secret || before is null ? null : Display(before.Value),
				Current = secret ? null : Display(now),
				Changed = before is null || Canonical(before.Value) != Canonical(now),
				AlsoUsedBy = secret
					? ReferencedIds(now)
						.SelectMany(id => usage.GetValueOrDefault(id) ?? [])
						.Where(user => user.Owner != owner.Owner)
						.Select(user => user.Name)
						.Distinct(StringComparer.Ordinal)
						.ToList()
					: []
			});
		}

		return result;
	}

	private static bool HasDeclaredSecret(IActionDefinition action, FlowBlockContent block)
	{
		var declared = DeclaredSecretWalker.Index(action.Parameters);
		return block.Parameters.Any(parameter => declared.TryGetValue(parameter.Name, out var definition)
			&& DeclaredSecretWalker.SecretIds(definition, parameter.Value).Count > 0);
	}

	private static JsonElement ValueOf(FlowBlockContent block, string name, ActionParameter? definition)
	{
		var stored = block.Parameters.FirstOrDefault(parameter
			=> string.Equals(parameter.Name, name, StringComparison.Ordinal));
		if (stored is not null)
		{
			return stored.Value;
		}

		var fallback = definition is null ? null : ActionParameterControlTypes.EditorDefault(definition);
		return fallback is null ? default : JsonSerializer.SerializeToElement(fallback);
	}

	private IEnumerable<Guid> ReferencedIds(JsonElement value)
	{
		switch (value.ValueKind)
		{
			case JsonValueKind.Object when SecretReferenceJson.TryGet(value, out var id):
				yield return _lineage?.Root(id) ?? id;
				break;
			case JsonValueKind.Object:
				foreach (var nested in value.EnumerateObject().SelectMany(property => ReferencedIds(property.Value)))
				{
					yield return nested;
				}

				break;
			case JsonValueKind.Array:
				foreach (var nested in value.EnumerateArray().SelectMany(ReferencedIds))
				{
					yield return nested;
				}

				break;
		}
	}

	private string Canonical(JsonElement value)
	{
		return FlowBlockNormalizer.Normalize(
			new FlowBlockContent(null, null, [new FlowBlockParameter("v", string.Empty, value)]),
			null,
			_lineage);
	}

	private static string? Display(JsonElement value)
	{
		var stripped = DeclaredSecretWalker.StripReferences(value);
		return stripped.ValueKind switch
		{
			JsonValueKind.Undefined or JsonValueKind.Null => null,
			JsonValueKind.String => stripped.GetString(),
			_ => stripped.GetRawText()
		};
	}

	private static string KindName(FlowOwnerKind kind)
	{
		return kind switch
		{
			FlowOwnerKind.Automation => "automation",
			FlowOwnerKind.Script => "script",
			_ => "widget"
		};
	}

	private sealed record OwnerBlocks(
		FlowOwner Owner,
		string Name,
		string? Context,
		IReadOnlyList<FlowBlockSnapshot> Blocks);
}
