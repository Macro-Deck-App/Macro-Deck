using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IFolderUiSessionOpener
{
	OpenFolderUiSessionResponse Open(OpenFolderUiSessionRequest request, string ownerPrincipal);
}

/// <summary>
/// Opens the <c>folder</c> surface for a folder whose view a provider renders. Mirrors
/// <see cref="WidgetUiSessionOpener" />, with one deliberate difference: the broker's provider id is the
/// owning integration's real id, not a synthetic per-folder one. A folder view may be served by a plugin,
/// and a plugin publishes snapshots under its own id - a synthetic id would fail the broker's ownership
/// check on the first patch. Session reuse therefore matches the folder on the surface instead of on the
/// provider id.
/// </summary>
public sealed class FolderUiSessionOpener : IFolderUiSessionOpener
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	private readonly IFolderCache _folderCache;
	private readonly IFolderViewRegistry _folderViews;
	private readonly UiSessionRegistry _registry;
	private readonly IUiSessionBroker _broker;

	public FolderUiSessionOpener(
		IFolderCache folderCache,
		IFolderViewRegistry folderViews,
		UiSessionRegistry registry,
		IUiSessionBroker broker)
	{
		_folderCache = folderCache;
		_folderViews = folderViews;
		_registry = registry;
		_broker = broker;
	}

	public OpenFolderUiSessionResponse Open(OpenFolderUiSessionRequest request, string ownerPrincipal)
	{
		ArgumentNullException.ThrowIfNull(request);
		ownerPrincipal ??= string.Empty;

		if (!Guid.TryParse(request.FolderId, out var folderId) ||
			_folderCache.GetFolderById(folderId) is not { } folder)
		{
			return Rejected(string.Empty,
				UiSessionErrorCodes.ProviderUnavailable,
				"That folder does not exist.");
		}

		if (BuiltInFolderViews.IsWidgetGrid(folder.ViewId))
		{
			return Rejected(BuiltInFolderViews.WidgetGrid,
				UiSessionErrorCodes.InvalidPayload,
				"The widget grid is rendered by the client, not as a session.");
		}

		var viewId = folder.ViewId!;

		// Not an error, and deliberately not a fault: the provider is simply not here. The client renders
		// its placeholder, and the folder keeps its view id and configuration untouched so reinstalling
		// the integration brings the folder straight back.
		if (!_folderViews.TryResolve(viewId, out var entry))
		{
			return Rejected(viewId, UiSessionErrorCodes.ProviderUnavailable, "That folder view is not available.");
		}

		var navigation = entry.Descriptor.ResolvedNavigation;

		if (FindExisting(entry.ProviderId, ownerPrincipal, folderId) is { } existing)
		{
			return Accepted(existing, viewId, navigation);
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Folder,
			SessionMode = UiSessionModes.Shared,
			Attributes = BuildAttributes(folderId, folder, viewId)
		};

		var ticket = _broker.Open(entry.ProviderId, surface, ownerPrincipal);

		return ticket.Accepted
			? Accepted(ticket.SessionId, viewId, navigation)
			: Rejected(viewId, ticket.Code ?? UiSessionErrorCodes.ProviderUnavailable, ticket.Message);
	}

	private string? FindExisting(string providerId, string ownerPrincipal, Guid folderId)
	{
		var wanted = folderId.ToString();

		foreach (var session in _registry.SessionsForProvider(providerId))
		{
			if (!string.Equals(session.OwnerPrincipal, ownerPrincipal, StringComparison.Ordinal) ||
				session.State is UiSessionState.Closed or UiSessionState.Invalidated ||
				!string.Equals(session.Surface.Kind, UiSurfaceKinds.Folder, StringComparison.Ordinal))
			{
				continue;
			}

			if (session.Surface.Attributes.TryGetValue(UiFolderSurfaceAttributes.FolderId, out var candidate) &&
				candidate.ValueKind == JsonValueKind.String &&
				string.Equals(candidate.GetString(), wanted, StringComparison.Ordinal))
			{
				return session.SessionId;
			}
		}

		return null;
	}

	private static Dictionary<string, JsonElement> BuildAttributes(Guid folderId, FolderEntity folder, string viewId)
		=> new(StringComparer.Ordinal)
		{
			[UiFolderSurfaceAttributes.FolderId] = JsonSerializer.SerializeToElement(folderId.ToString()),
			[UiFolderSurfaceAttributes.FolderName] = JsonSerializer.SerializeToElement(folder.Name),
			[UiFolderSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(viewId),
			[UiFolderSurfaceAttributes.Configuration] = ParseStoredConfiguration(folder.ViewConfiguration)
		};

	private static JsonElement ParseStoredConfiguration(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return _emptyObject;
		}

		try
		{
			using var document = JsonDocument.Parse(json);
			return document.RootElement.ValueKind == JsonValueKind.Object
				? document.RootElement.Clone()
				: _emptyObject;
		}
		catch (JsonException)
		{
			return _emptyObject;
		}
	}

	private static OpenFolderUiSessionResponse Accepted(string sessionId, string viewId, string navigation)
		=> new() { Accepted = true, SessionId = sessionId, ViewId = viewId, Navigation = navigation };

	private static OpenFolderUiSessionResponse Rejected(string viewId, string code, string? message)
		=> new() { Accepted = false, SessionId = string.Empty, ViewId = viewId, Code = code, Message = message };
}
