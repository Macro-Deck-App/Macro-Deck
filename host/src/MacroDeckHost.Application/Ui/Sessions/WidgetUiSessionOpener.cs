using System.Text.Json;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IWidgetUiSessionOpener
{
	UiSessionOpenTicket Open(OpenWidgetUiSessionRequest request, string ownerPrincipal, bool isAdmin);
}

public sealed class WidgetUiSessionOpener : IWidgetUiSessionOpener
{
	private static readonly JsonElement _emptyObject = JsonDocument.Parse("{}").RootElement;

	private readonly IFolderCache _folderCache;
	private readonly IProfileCache _profileCache;
	private readonly IWidgetDataSchemaProvider _schemas;
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly UiSessionRegistry _registry;
	private readonly IUiSessionBroker _broker;

	public WidgetUiSessionOpener(
		IFolderCache folderCache,
		IProfileCache profileCache,
		IWidgetDataSchemaProvider schemas,
		IWidgetTypeRegistry widgetTypes,
		UiSessionRegistry registry,
		IUiSessionBroker broker)
	{
		_folderCache = folderCache;
		_profileCache = profileCache;
		_schemas = schemas;
		_widgetTypes = widgetTypes;
		_registry = registry;
		_broker = broker;
	}

	public UiSessionOpenTicket Open(OpenWidgetUiSessionRequest request, string ownerPrincipal, bool isAdmin)
	{
		ArgumentNullException.ThrowIfNull(request);
		ownerPrincipal ??= string.Empty;

		return string.IsNullOrEmpty(request.WidgetId)
			? OpenPreview(request, ownerPrincipal, isAdmin)
			: OpenLive(request, ownerPrincipal);
	}

	private UiSessionOpenTicket OpenLive(OpenWidgetUiSessionRequest request, string ownerPrincipal)
	{
		if (!Guid.TryParse(request.WidgetId, out var widgetId))
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"That widget does not exist.");
		}

		var widget = FindWidget(widgetId);
		var folder = FindFolderOf(widgetId);
		if (widget is null)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"That widget does not exist.");
		}

		// A type a provider owns is served by that provider, so the session opens under the provider's own
		// id rather than a synthetic per-widget one: the broker's ownership check, the plugin-session
		// lookup and provider invalidation all compare against the real owner, and a synthetic id would
		// fail every one of them on the first patch. FolderUiSessionOpener made the same call for the same
		// reason, and reuse is matched on the surface instead.
		var owner = ProviderOwnerOf(widget.Type);

		var providerId = owner ??
			(request.Ghost
				? WidgetUiProviderRegistry.GhostProviderIdFor(widgetId, ownerPrincipal)
				: WidgetUiProviderRegistry.ProviderIdFor(widgetId, ownerPrincipal));

		var existing = owner is null
			? FindExisting(providerId, ownerPrincipal)
			: FindExistingSurface(providerId,
				ownerPrincipal,
				UiSurfaceKinds.Widget,
				candidate => Matches(candidate, UiWidgetSurfaceAttributes.WidgetId, widgetId.ToString()) &&
					IsFlagged(candidate, UiWidgetSurfaceAttributes.Ghost) == request.Ghost);

		if (existing is { } opened)
		{
			return opened;
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = BuildLiveAttributes(widgetId, widget, folder, request.Ghost)
		};

		return _broker.Open(providerId, surface, ownerPrincipal);
	}

	private UiSessionOpenTicket OpenPreview(OpenWidgetUiSessionRequest request, string ownerPrincipal, bool isAdmin)
	{
		if (!isAdmin)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.SessionForbidden,
				"Only an admin session may preview a widget.");
		}

		// Clients have always spelled a widget type in kebab-case ("action-button") while the stored id
		// reads "ActionButton"; the registry resolves either, so a multi-word type is previewable.
		if (_widgetTypes.Resolve(request.WidgetType) is not { } widgetType)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"That widget type does not exist.");
		}

		var draft = request.Data ?? _emptyObject;
		if (draft.ValueKind != JsonValueKind.Object)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.InvalidPayload,
				"The preview data must be a JSON object.");
		}

		if (_schemas.TryGet(widgetType, out var schema) && WidgetDataSchema.Validate(schema, draft).Count > 0)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.InvalidPayload,
				"That preview data does not match the widget's schema.");
		}

		// A scope naming no stored widget is dropped rather than refused: a widget being created has no
		// id to name yet, and that preview has to keep opening exactly as it did.
		var variableScope = Guid.TryParse(request.VariableScopeWidgetId, out var scopeWidgetId) &&
			FindWidget(scopeWidgetId) is not null
				? scopeWidgetId
				: (Guid?)null;

		var owner = ProviderOwnerOf(widgetType);

		var providerId = owner ??
			(request.Sample
				? WidgetUiProviderRegistry.SamplePreviewProviderIdFor(widgetType, ownerPrincipal)
				: WidgetUiProviderRegistry.PreviewProviderIdFor(widgetType, ownerPrincipal, variableScope));

		// The three things a preview's synthetic id encodes are matched off the surface instead, so a
		// provider's picker sample and an editor's draft of the same type keep their own sessions.
		var existing = owner is null
			? FindExisting(providerId, ownerPrincipal)
			: FindExistingSurface(providerId,
				ownerPrincipal,
				UiSurfaceKinds.Preview,
				candidate => Matches(candidate, UiWidgetSurfaceAttributes.WidgetType, widgetType) &&
					IsFlagged(candidate, UiWidgetSurfaceAttributes.Sample) == request.Sample &&
					Matches(candidate,
						UiWidgetSurfaceAttributes.VariableScopeWidgetId,
						variableScope?.ToString()));

		if (existing is { } opened)
		{
			return opened;
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Preview,
			SessionMode = UiSessionModes.Shared,
			Attributes = BuildPreviewAttributes(widgetType, draft, request.Sample, variableScope)
		};

		return _broker.Open(providerId, surface, ownerPrincipal);
	}

	private WidgetEntity? FindWidget(Guid widgetId)
		=> _folderCache.GetAllFolders().SelectMany(folder => folder.Widgets).FirstOrDefault(w => w.Id == widgetId);

	/// <summary>The integration or plugin that serves this widget type, or <c>null</c> for a built-in type
	/// the host draws itself and for a type nothing provides.</summary>
	private string? ProviderOwnerOf(string widgetType)
		=> _widgetTypes.TryResolve(widgetType, out var entry) && !entry.IsBuiltIn
			? entry.ProviderId
			: null;

	// A synthetic provider id already names exactly one session, so matching the owner is all it takes.
	private UiSessionOpenTicket? FindExisting(string providerId, string ownerPrincipal)
	{
		foreach (var session in _registry.SessionsForProvider(providerId))
		{
			if (string.Equals(session.OwnerPrincipal, ownerPrincipal, StringComparison.Ordinal) &&
				session.State is not (UiSessionState.Closed or UiSessionState.Invalidated))
			{
				return UiSessionOpenTicket.Opened(session.SessionId);
			}
		}

		return null;
	}

	// One provider id backs every widget it serves, so what the session is for lives on the surface.
	private UiSessionOpenTicket? FindExistingSurface(
		string providerId,
		string ownerPrincipal,
		string surfaceKind,
		Func<UiSurface, bool> matches)
	{
		foreach (var session in _registry.SessionsForProvider(providerId))
		{
			if (!string.Equals(session.OwnerPrincipal, ownerPrincipal, StringComparison.Ordinal) ||
				session.State is UiSessionState.Closed or UiSessionState.Invalidated ||
				!string.Equals(session.Surface.Kind, surfaceKind, StringComparison.Ordinal))
			{
				continue;
			}

			if (matches(session.Surface))
			{
				return UiSessionOpenTicket.Opened(session.SessionId);
			}
		}

		return null;
	}

	private static bool Matches(UiSurface surface, string key, string? expected)
		=> surface.Attributes.TryGetValue(key, out var candidate)
			? candidate.ValueKind == JsonValueKind.String &&
			string.Equals(candidate.GetString(), expected, StringComparison.Ordinal)
			: expected is null;

	private static bool IsFlagged(UiSurface surface, string key)
		=> surface.Attributes.TryGetValue(key, out var candidate) && candidate.ValueKind == JsonValueKind.True;

	private Dictionary<string, JsonElement> BuildLiveAttributes(
		Guid widgetId,
		WidgetEntity widget,
		FolderEntity? folder,
		bool ghost)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(widgetId.ToString()),
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widget.Type),
			[UiWidgetSurfaceAttributes.Data] = ParseStoredData(widget.Data),
			[UiWidgetSurfaceAttributes.CornerRadius] = JsonSerializer.SerializeToElement(CornerRadiusOf(folder))
		};

		if (ghost)
		{
			attributes[UiWidgetSurfaceAttributes.Ghost] = JsonSerializer.SerializeToElement(true);
		}

		return attributes;
	}

	/// <summary>
	/// The radius this widget's tile is drawn with: the folder's own where it states one, otherwise the
	/// profile's default, otherwise the built-in. The same inheritance the reader resolves, resolved once
	/// here so a view never has to know what a folder or a profile is (ADR 0064).
	/// </summary>
	private int CornerRadiusOf(FolderEntity? folder)
	{
		if (folder?.WidgetBorderRadius is { } stated)
		{
			return stated;
		}

		var profile = folder is null ? null : _profileCache.GetById(folder.ProfileId);

		return profile?.DefaultWidgetBorderRadius ?? GridDefaults.WidgetBorderRadius;
	}

	private FolderEntity? FindFolderOf(Guid widgetId)
		=> _folderCache.GetAllFolders().FirstOrDefault(folder => folder.Widgets.Any(w => w.Id == widgetId));

	private static Dictionary<string, JsonElement> BuildPreviewAttributes(string widgetType,
		JsonElement data,
		bool sample,
		Guid? variableScopeWidgetId)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
			[UiWidgetSurfaceAttributes.Data] = data
		};

		if (variableScopeWidgetId is { } scope)
		{
			attributes[UiWidgetSurfaceAttributes.VariableScopeWidgetId]
				= JsonSerializer.SerializeToElement(scope.ToString());
		}

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}

		return attributes;
	}

	private static JsonElement ParseStoredData(string? json)
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
}
