using System.Reflection;
using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Previews;
using MacroDeckHost.Application.Ui.Sessions.InProcess;

namespace MacroDeckHost.Widgets.DeveloperPreviews;

public sealed class IntegrationUiPreviewSource : IUiPreviewSource
{
	private readonly Lazy<UiPreviewScanResult> _scan;

	public IntegrationUiPreviewSource(Assembly assembly)
	{
		ArgumentNullException.ThrowIfNull(assembly);

		_scan = new Lazy<UiPreviewScanResult>(() => UiPreviewCatalog.Scan(assembly));
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.DeveloperPreview, SessionMode = UiSessionModes.Exclusive }
	];

	public IReadOnlyList<UiPreviewDescriptor> Previews =>
	[
		.. _scan.Value.Registrations.Select(registration => new UiPreviewDescriptor
		{
			Id = registration.Declaration.Id,
			View = registration.Declaration.View,
			Scenario = registration.Declaration.Scenario,
			Profile = registration.Declaration.Profile
		})
	];

	public IReadOnlyList<UiPreviewSkipped> Skipped =>
	[
		.. _scan.Value.Diagnostics.Select(diagnostic => new UiPreviewSkipped
		{
			Member = diagnostic.Member, Reason = diagnostic.Reason
		})
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		if (request.Surface.Kind != UiSurfaceKinds.DeveloperPreview
			|| !request.Surface.Attributes.TryGetValue(UiDeveloperPreviewSurfaceAttributes.PreviewId, out var idElement)
			|| idElement.ValueKind != JsonValueKind.String
			|| idElement.GetString() is not { } previewId
			|| _scan.Value.Registrations.FirstOrDefault(registration =>
				string.Equals(registration.Declaration.Id, previewId, StringComparison.Ordinal)) is not { } match)
		{
			return Task.FromResult<IUiSession?>(null);
		}

		return Task.FromResult<IUiSession?>(new WidgetUiPreviewSession(match.Create(request.Surface)));
	}
}
