namespace MacroDeck.Ui.Model.Patches;

/// <summary>
/// The result of checking whether something can apply. Carries both a patch-level sequencing verdict
/// (<see cref="UiPatchSequencing.CheckRevisions" />) and a per-operation verdict
/// (<see cref="UiPatchOperationValidation.Validate" />) - the same accept-or-reject-with-reason shape
/// serves both, so one type carries it rather than two. A computation result, not a wire type: it never
/// crosses the wire on its own, so it carries no canonical serialization contract. Mirrors the plugin
/// protocol's own negotiation-result shape.
/// </summary>
public sealed record UiPatchOutcome
{
	/// <summary>Whether the patch or operation can apply.</summary>
	public required bool IsApplicable { get; init; }

	/// <summary>Why the patch or operation cannot apply, set exactly when <see cref="IsApplicable" /> is
	/// <c>false</c>.</summary>
	public string? RejectionReason { get; init; }

	/// <summary>Builds an applicable outcome.</summary>
	public static UiPatchOutcome Accept() => new() { IsApplicable = true };

	/// <summary>Builds an inapplicable outcome carrying why.</summary>
	public static UiPatchOutcome Reject(string reason)
		=> new() { IsApplicable = false, RejectionReason = reason };
}
