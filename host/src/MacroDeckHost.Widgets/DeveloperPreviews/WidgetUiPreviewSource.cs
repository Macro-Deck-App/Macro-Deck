using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Widgets.Clock;

namespace MacroDeckHost.Widgets.DeveloperPreviews;

/// <summary>
/// Exposes every first-party widget preview scenario. Scanned off <c>MacroDeckHost.Widgets</c> itself -
/// the only host assembly that uses the <c>MacroDeck.Ui</c> DSL for anything a Developer Tools client
/// would want to preview.
/// </summary>
public sealed class WidgetUiPreviewSource : IUiPreviewSource
{
	private readonly Lazy<UiPreviewScanResult> _scan
		= new(() => UiPreviewCatalog.Scan(typeof(ClockWidgetView).Assembly));

	public WidgetUiPreviewSource(IUiResourceStore resources)
	{
		ArgumentNullException.ThrowIfNull(resources);

		// Before any scenario can be built: a scenario registers its icons in this store, and one
		// registered anywhere else is never served to the client.
		WidgetPreviewResources.Use(resources);
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new()
			{ Kind = UiSurfaceKinds.DeveloperPreview, SessionMode = UiSessionModes.Exclusive },
	];

	public IReadOnlyList<UiPreviewDescriptor> Previews =>
	[
		.. _scan.Value.Registrations.Select(registration => new UiPreviewDescriptor
		{
			Id = registration.Declaration.Id,
			View = registration.Declaration.View,
			Scenario = registration.Declaration.Scenario,
			Profile = registration.Declaration.Profile,
		}),
	];

	public IReadOnlyList<UiPreviewSkipped> Skipped =>
	[
		.. _scan.Value.Diagnostics.Select(diagnostic => new UiPreviewSkipped
		{
			Member = diagnostic.Member, Reason = diagnostic.Reason,
		}),
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind != UiSurfaceKinds.DeveloperPreview ||
			!request.Surface.Attributes.TryGetValue(UiDeveloperPreviewSurfaceAttributes.PreviewId, out var idElement) ||
			idElement.ValueKind != JsonValueKind.String)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var previewId = idElement.GetString();
		var match = _scan.Value.Registrations
			.FirstOrDefault(registration =>
				string.Equals(registration.Declaration.Id, previewId, StringComparison.Ordinal));

		if (match is null)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		var instance = match.Create(request.Surface);

		return Task.FromResult<IUiSession?>(new WidgetUiPreviewSession(instance));
	}
}
