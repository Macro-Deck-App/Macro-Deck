using MacroDeck.Ui.Model.Nodes;

namespace MacroDeck.Ui.Testing;

/// <summary>
/// The result of <see cref="UiTreeApplier.Apply" />: whether the patch applied, why not when it did not, and
/// the resulting tree - or the unchanged input tree when the patch was rejected.
/// </summary>
public sealed record UiApplyResult
{
	/// <summary>Whether every operation in the patch applied. <c>false</c> means the whole patch was
	/// discarded atomically; see <see cref="Tree" /> for what that means for the tree.</summary>
	public required bool IsApplied { get; init; }

	/// <summary>Why the patch could not apply, set exactly when <see cref="IsApplied" /> is <c>false</c>.
	/// </summary>
	public string? RejectionReason { get; init; }

	/// <summary>The resulting tree when <see cref="IsApplied" /> is <c>true</c>. When <see cref="IsApplied" />
	/// is <c>false</c> this is the same <see cref="UiTree" /> instance that was passed to
	/// <see cref="UiTreeApplier.Apply" />, reference-equal to the input, so a caller can never observe a
	/// half-applied tree.</summary>
	public required UiTree Tree { get; init; }
}
