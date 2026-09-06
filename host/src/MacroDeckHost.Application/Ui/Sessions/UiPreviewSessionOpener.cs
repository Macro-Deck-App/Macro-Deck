using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiPreviews;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IUiPreviewSessionOpener
{
	UiSessionOpenTicket Open(OpenUiPreviewSessionRequest request, string ownerPrincipal, bool isAdmin);
}

/// <summary>
/// Opens a developer preview session, addressed by the scenario id <see cref="MacroDeck.Ui.Previews.UiPreviewCatalog" />
/// assigns. Kept separate from <see cref="WidgetUiSessionOpener" /> because the two differ in every way
/// that matters here: a preview is admin-only, is checked for that before anything is resolved or built,
/// and is always rebuilt on refresh rather than reused.
/// </summary>
public sealed class UiPreviewSessionOpener : IUiPreviewSessionOpener
{
	private readonly IEnumerable<IUiPreviewSource> _sources;
	private readonly IIntegrationRegistry _integrations;
	private readonly IRemotePluginSnapshotStore _snapshots;
	private readonly UiSessionRegistry _registry;
	private readonly IUiSessionBroker _broker;

	public UiPreviewSessionOpener(
		IEnumerable<IUiPreviewSource> sources,
		IIntegrationRegistry integrations,
		IRemotePluginSnapshotStore snapshots,
		UiSessionRegistry registry,
		IUiSessionBroker broker)
	{
		_sources = sources;
		_integrations = integrations;
		_snapshots = snapshots;
		_registry = registry;
		_broker = broker;
	}

	public UiSessionOpenTicket Open(OpenUiPreviewSessionRequest request, string ownerPrincipal, bool isAdmin)
	{
		ArgumentNullException.ThrowIfNull(request);
		ownerPrincipal ??= string.Empty;

		// Checked first, before anything resolves or is built: a non-admin must not learn whether a
		// preview id exists, and a scenario must never run on their behalf.
		if (!isAdmin)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.SessionForbidden,
				"Only an admin session may open a developer preview.");
		}

		var previewId = request.PreviewId;
		if (string.IsNullOrEmpty(previewId))
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"That preview does not exist.");
		}

		var providerId = ResolveProviderId(previewId, ownerPrincipal, out var profile);
		if (providerId is null)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"That preview does not exist.");
		}

		// A refresh is "open the same preview again", not "attach to what is already open" - the caller
		// wants a scenario nothing has touched yet, so any prior session for this preview and principal is
		// closed rather than reused.
		CloseExisting(providerId, ownerPrincipal);

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.DeveloperPreview,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = BuildAttributes(previewId, profile)
		};

		return _broker.Open(providerId, surface, ownerPrincipal);
	}

	private string? ResolveProviderId(string previewId, string ownerPrincipal, out string profile)
	{
		var firstParty = _sources
			.SelectMany(source => source.Previews)
			.FirstOrDefault(preview => string.Equals(preview.Id, previewId, StringComparison.Ordinal));

		if (firstParty is not null)
		{
			profile = firstParty.Profile;
			return UiPreviewProviderRegistry.ProviderIdFor(previewId, ownerPrincipal);
		}

		foreach (var integration in _integrations.Integrations)
		{
			if (!_snapshots.Has(integration.Id))
			{
				continue;
			}

			var match = _snapshots.GetSnapshot(integration.Id).UiPreviews
				.FirstOrDefault(preview => string.Equals(preview.Id, previewId, StringComparison.Ordinal));

			if (match is not null)
			{
				profile = match.Profile;

				// A plugin's own id is the provider id, so RemoteUiProviderRegistry resolves it exactly as
				// it does for every other plugin-served UI session - the scenario id travels only in the
				// surface attributes.
				return integration.Id;
			}
		}

		profile = string.Empty;
		return null;
	}

	private void CloseExisting(string providerId, string ownerPrincipal)
	{
		foreach (var session in _registry.SessionsForProvider(providerId))
		{
			if (string.Equals(session.OwnerPrincipal, ownerPrincipal, StringComparison.Ordinal) &&
				session.State is not (UiSessionState.Closed or UiSessionState.Invalidated))
			{
				_broker.CloseOwned(session.SessionId, ownerPrincipal, "The preview was refreshed.");
			}
		}
	}

	private static Dictionary<string, JsonElement> BuildAttributes(string previewId, string profile)
		=> new(StringComparer.Ordinal)
		{
			[UiDeveloperPreviewSurfaceAttributes.PreviewId] = JsonSerializer.SerializeToElement(previewId),
			[UiDeveloperPreviewSurfaceAttributes.Profile] = JsonSerializer.SerializeToElement(profile)
		};
}
