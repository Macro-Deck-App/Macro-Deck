namespace MacroDeck.Ui.Model.Patches;

/// <summary>
/// Pure ordering and atomicity rules for applying a <see cref="UiPatch" /> to a tree currently at a
/// known revision. Named for what it can actually see: it takes only a revision and the patch, so it
/// cannot evaluate "unknown target id" or any other tree-dependent rule - promising that in a docstring
/// would be a lie the signature cannot keep. See <see cref="UiPatchOperationValidation" /> for the
/// context-free per-operation rules, and <see cref="UiPatchOperation" />'s remarks for the tree-dependent
/// rules that stay a documented renderer contract with no API here.
/// </summary>
public static class UiPatchSequencing
{
	/// <summary>
	/// A patch applies only when <paramref name="currentRevision" /> equals
	/// <see cref="UiPatch.FromRevision" />, <see cref="UiPatch.ToRevision" /> is strictly greater than
	/// <see cref="UiPatch.FromRevision" />, and <see cref="UiPatch.Operations" /> is non-empty.
	///
	/// <para>
	/// The revision advance is checked by direct comparison, never by subtraction: <c>ToRevision -
	/// FromRevision &gt; 0</c> overflows for values such as <c>FromRevision = int.MaxValue,
	/// ToRevision = int.MinValue</c> and would wrongly accept a patch that does not advance the
	/// revision at all.
	/// </para>
	/// </summary>
	public static UiPatchOutcome CheckRevisions(int currentRevision, UiPatch patch)
	{
		if (currentRevision != patch.FromRevision)
		{
			return UiPatchOutcome.Reject(
				$"The patch applies from revision {patch.FromRevision}, but the current revision is " +
				$"{currentRevision}.");
		}

		if (patch.ToRevision <= patch.FromRevision)
		{
			return UiPatchOutcome.Reject("The patch does not advance the revision.");
		}

		if (patch.Operations.Count == 0)
		{
			return UiPatchOutcome.Reject("The patch carries no operations.");
		}

		return UiPatchOutcome.Accept();
	}
}
