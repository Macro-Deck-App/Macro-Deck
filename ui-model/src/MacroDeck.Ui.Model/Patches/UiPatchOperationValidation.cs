using MacroDeck.Ui.Model.Identity;

namespace MacroDeck.Ui.Model.Patches;

/// <summary>
/// The context-free rules a single <see cref="UiPatchOperation" /> must satisfy, evaluable without a
/// tree: an unknown <see cref="UiPatchOperation.Op" />, an invalid <see cref="UiPatchOperation.NodeId" />,
/// a key present in both <see cref="UiPatchOperation.Properties" /> and
/// <see cref="UiPatchOperation.RemovedProperties" />, an
/// <see cref="UiPatchOperations.InsertNode" /> whose <see cref="UiPatchOperation.NodeId" /> does not
/// equal its <see cref="UiPatchOperation.Node" />'s id, and the fields each operation requires present.
///
/// <para>
/// Tree-dependent rules - index bounds, root-targeting operations, apply order, atomicity - cannot be
/// evaluated without a tree and stay a documented renderer contract with no API; see
/// <see cref="UiPatchOperation" />'s remarks.
/// </para>
///
/// <para>
/// Every operation requires a valid <see cref="UiPatchOperation.NodeId" /> and a known
/// <see cref="UiPatchOperation.Op" />. Beyond that, the required and optional fields are frozen per
/// operation:
/// </para>
///
/// <list type="bullet">
/// <item><see cref="UiPatchOperations.SetProperties" /> requires at least one of
/// <see cref="UiPatchOperation.Properties" /> or <see cref="UiPatchOperation.RemovedProperties" />.</item>
/// <item><see cref="UiPatchOperations.InsertNode" /> requires <see cref="UiPatchOperation.ParentId" /> and
/// <see cref="UiPatchOperation.Node" />, with <see cref="UiPatchOperation.NodeId" /> equal to
/// <see cref="UiPatchOperation.Node" />'s id. <see cref="UiPatchOperation.Index" /> is optional; a
/// <c>null</c> index appends.</item>
/// <item><see cref="UiPatchOperations.RemoveNode" /> requires only <see cref="UiPatchOperation.NodeId" />.</item>
/// <item><see cref="UiPatchOperations.ReplaceNode" /> requires <see cref="UiPatchOperation.Node" />.</item>
/// <item><see cref="UiPatchOperations.MoveNode" /> requires <see cref="UiPatchOperation.ParentId" />.
/// <see cref="UiPatchOperation.Index" /> is optional; a <c>null</c> index appends to the new parent.</item>
/// </list>
/// </summary>
public static class UiPatchOperationValidation
{
	/// <summary>Checks one operation against the context-free rules. Never throws, including for an
	/// unrecognised <see cref="UiPatchOperation.Op" />.</summary>
	public static UiPatchOutcome Validate(UiPatchOperation operation)
	{
		if (!UiPatchOperations.IsKnown(operation.Op))
		{
			return UiPatchOutcome.Reject($"Unknown patch operation \"{operation.Op}\".");
		}

		if (!UiIdentifier.IsValid(operation.NodeId))
		{
			return UiPatchOutcome.Reject($"\"{operation.NodeId}\" is not a valid node id.");
		}

		if (HasConflictingPropertyKeys(operation))
		{
			return UiPatchOutcome.Reject("A property key appears in both Properties and RemovedProperties.");
		}

		return operation.Op switch
		{
			UiPatchOperations.SetProperties => ValidateSetProperties(operation),
			UiPatchOperations.InsertNode => ValidateInsertNode(operation),
			UiPatchOperations.ReplaceNode => ValidateReplaceNode(operation),
			UiPatchOperations.MoveNode => ValidateMoveNode(operation),
			_ => UiPatchOutcome.Accept(),
		};
	}

	private static bool HasConflictingPropertyKeys(UiPatchOperation operation)
	{
		if (operation.Properties is null || operation.RemovedProperties is null)
		{
			return false;
		}

		foreach (var removedKey in operation.RemovedProperties)
		{
			if (operation.Properties.ContainsKey(removedKey))
			{
				return true;
			}
		}

		return false;
	}

	private static UiPatchOutcome ValidateSetProperties(UiPatchOperation operation)
		=> operation.Properties is null && operation.RemovedProperties is null
			? UiPatchOutcome.Reject("set-properties requires at least one of Properties or RemovedProperties.")
			: UiPatchOutcome.Accept();

	private static UiPatchOutcome ValidateInsertNode(UiPatchOperation operation)
	{
		if (operation.Node is null)
		{
			return UiPatchOutcome.Reject("insert-node requires Node.");
		}

		if (!string.Equals(operation.NodeId, operation.Node.Id, StringComparison.Ordinal))
		{
			return UiPatchOutcome.Reject("insert-node's NodeId must equal Node.Id.");
		}

		return operation.ParentId is null
			? UiPatchOutcome.Reject("insert-node requires ParentId.")
			: UiPatchOutcome.Accept();
	}

	private static UiPatchOutcome ValidateReplaceNode(UiPatchOperation operation)
		=> operation.Node is null
			? UiPatchOutcome.Reject("replace-node requires Node.")
			: UiPatchOutcome.Accept();

	private static UiPatchOutcome ValidateMoveNode(UiPatchOperation operation)
		=> operation.ParentId is null
			? UiPatchOutcome.Reject("move-node requires ParentId.")
			: UiPatchOutcome.Accept();
}
