using MacroDeck.Sdk.Ui;

namespace MacroDeckHost.Application.Ui.Sessions.InProcess;

/// <summary>One declared preview scenario, expressed without the <c>MacroDeck.Ui</c> DSL - see
/// <see cref="IUiPreviewSource" />.</summary>
public sealed record UiPreviewDescriptor
{
	public required string Id { get; init; }

	public required string View { get; init; }

	public required string Scenario { get; init; }

	public required string Profile { get; init; }
}

/// <summary>A preview method that carried the attribute but could not be registered - see
/// <c>MacroDeck.Ui.Previews.UiPreviewDiagnostic</c>, which this mirrors.</summary>
public sealed record UiPreviewSkipped
{
	public required string Member { get; init; }

	public required string Reason { get; init; }
}

/// <summary>An in-process <see cref="IUiProvider" /> that also declares developer preview scenarios.
/// <c>MacroDeckHost.Application</c> does not reference the <c>MacroDeck.Ui</c> DSL, so a preview's
/// declaration crosses into it through <see cref="UiPreviewDescriptor" /> and <see cref="UiPreviewSkipped" />
/// rather than through the DSL's own types.</summary>
public interface IUiPreviewSource : IUiProvider
{
	IReadOnlyList<UiPreviewDescriptor> Previews { get; }

	IReadOnlyList<UiPreviewSkipped> Skipped { get; }
}
