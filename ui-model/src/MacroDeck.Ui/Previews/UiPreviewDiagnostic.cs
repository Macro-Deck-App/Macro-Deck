namespace MacroDeck.Ui.Previews;

/// <summary>A method that carries <see cref="UiPreviewAttribute" /> but could not be registered.</summary>
/// <remarks>
/// Reported rather than thrown: discovery runs beside the surfaces a provider really serves, so a
/// malformed preview has to cost only itself. Developer Tools shows these so a preview that silently
/// never appears is still explainable.
/// </remarks>
public sealed record UiPreviewDiagnostic
{
	/// <summary>The method that was skipped, as <c>Namespace.Type.Method</c>.</summary>
	public required string Member { get; init; }

	/// <summary>Why it was skipped, in terms of the contract it failed to meet.</summary>
	public required string Reason { get; init; }
}
