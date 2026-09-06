using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Application.Devices.Surfaces;

/// <summary>
/// What a device should render right now, plus what the session has to subscribe to keep it current.
/// <see cref="Revision" /> is not part of it: the session owns the counter and only advances it once
/// the projection is known to differ from the last one pushed.
/// </summary>
public sealed record DeviceSurfaceProjection
{
	public static readonly DeviceSurfaceProjection Empty = new()
	{
		Surface = DeviceSurface.Empty,
		ProfileId = null,
		FolderId = null,
		Subscriptions = [],
		WidgetFolderIds = new Dictionary<string, string>(StringComparer.Ordinal)
	};

	public required DeviceSurface Surface { get; init; }

	public required string? ProfileId { get; init; }

	public required string? FolderId { get; init; }

	/// <summary>The widgets the session subscribes state and label pushes for.</summary>
	public required IReadOnlyList<DeviceSurfaceSubscription> Subscriptions { get; init; }

	/// <summary>
	/// The folder that owns each widget on the surface, keyed by widget id. Not the folder being
	/// rendered: a pinned widget is shown here but belongs to another folder, and its trigger has to be
	/// addressed to the folder it belongs to.
	/// </summary>
	public required IReadOnlyDictionary<string, string> WidgetFolderIds { get; init; }

	public string? FolderName { get; init; }
}

/// <summary>One widget the session watches, keyed the way each publisher keys its subscribers.</summary>
public sealed record DeviceSurfaceSubscription(Guid WidgetId, string StateId);

/// <summary>
/// Projects a device's assigned profile and current folder into the surface it should render. Reads
/// folders as DTOs through <see cref="IProfileRegistry" /> rather than as entities, so a virtual
/// profile projects through exactly the same path a stored one does.
/// </summary>
public sealed class DeviceSurfaceBuilder
{
	private readonly IProfileRegistry _profiles;
	private readonly IWidgetStateService _widgetStates;
	private readonly IWidgetIconService _widgetIcons;
	private readonly ILabelTextService _labelText;
	private readonly ILocalizationResolver _localization;
	private readonly IAppPreferenceService _preferences;
	private readonly IIconPackCache _icons;

	public DeviceSurfaceBuilder(
		IProfileRegistry profiles,
		IWidgetStateService widgetStates,
		IWidgetIconService widgetIcons,
		ILabelTextService labelText,
		ILocalizationResolver localization,
		IAppPreferenceService preferences,
		IIconPackCache icons)
	{
		_icons = icons;
		_profiles = profiles;
		_widgetStates = widgetStates;
		_widgetIcons = widgetIcons;
		_labelText = labelText;
		_localization = localization;
		_preferences = preferences;
	}

	public async Task<DeviceSurfaceProjection> BuildAsync(
		DeviceEntity device,
		string? currentProfileId,
		string? currentFolderId,
		CancellationToken cancellationToken)
	{
		if (ResolveProfileId(currentProfileId, device.StartupProfileId) is not { } profileId ||
			ResolveProfile(profileId) is not { } profile ||
			_profiles.GetFoldersForProfile(profileId) is not { Count: > 0 } folders)
		{
			return DeviceSurfaceProjection.Empty;
		}

		var folder = folders.FirstOrDefault(candidate =>
				string.Equals(candidate.Id, currentFolderId, StringComparison.Ordinal)) ??
			StartFolderResolver.SelectStartFolder(folders);
		if (folder is null)
		{
			return DeviceSurfaceProjection.Empty;
		}

		var culture = (await _preferences.GetLocalization()).Culture;
		var widgets = new List<DeviceSurfaceWidget>();
		var subscriptions = new List<DeviceSurfaceSubscription>();
		var widgetFolderIds = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (widget, ownerFolderId) in SurfaceFolderChain.DisplayedWidgets(folder, folders))
		{
			widgets.Add(await BuildWidget(widget, culture, subscriptions, cancellationToken));
			widgetFolderIds[widget.Id] = ownerFolderId;
		}

		var surface = new DeviceSurface
		{
			Revision = 1,
			Profile = new DeviceSurfaceProfile(profile.Id, profile.Name),
			Folder = new DeviceSurfaceFolder(folder.Id, folder.Name, folder.ParentId, folder.ParentId is null),
			Layout = new DeviceSurfaceLayout
			{
				Rows = SurfaceFolderChain.ResolveRows(folder, folders, profile.DefaultRows),
				Columns = SurfaceFolderChain.ResolveColumns(folder, folders, profile.DefaultColumns),
				WidgetSpacing = SurfaceFolderChain.ResolveWidgetSpacing(folder, folders, profile.DefaultWidgetSpacing),
				WidgetBorderRadius = SurfaceFolderChain.ResolveWidgetBorderRadius(folder,
					folders,
					profile.DefaultWidgetBorderRadius),
				BackgroundColor = SurfaceFolderChain.ResolveBackgroundColor(folder,
					profile.DefaultBackgroundColor),
				// Echoed verbatim: the host never parses it and never fits the grid or the widget
				// positions to the device's physical capabilities.
				LayoutReference = device.LayoutReference
			},
			Widgets = widgets
		};

		return new DeviceSurfaceProjection
		{
			Surface = surface,
			ProfileId = profile.Id,
			FolderId = folder.Id,
			FolderName = folder.Name,
			Subscriptions = subscriptions,
			WidgetFolderIds = widgetFolderIds
		};
	}

	private static IReadOnlyList<DeviceInteractionKind> SupportedInteractions(Widget widget)
	{
		var triggerTypes = WidgetFlowsJson.TriggerTypes(widget.Data);
		var hasPressFlow = triggerTypes.Any(type =>
			string.Equals(type, WidgetTriggerTypes.ShortPress, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(type, WidgetTriggerTypes.LongPress, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(type, WidgetTriggerTypes.TouchStart, StringComparison.OrdinalIgnoreCase) ||
			string.Equals(type, WidgetTriggerTypes.TouchEnd, StringComparison.OrdinalIgnoreCase));

		return hasPressFlow
			?
			[
				DeviceInteractionKind.Press, DeviceInteractionKind.Release, DeviceInteractionKind.ShortPress,
				DeviceInteractionKind.LongPress
			]
			: [];
	}

	private static string? ReadString(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	private static int? ReadInt(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : null;

	private static double? ReadDouble(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<double>(out var number) ? number : null;

	private static string? Pick(JsonObject? state, JsonObject root, string key)
		=> ReadString(state, key) is { Length: > 0 } value ? value : ReadString(root, key);

	/// <summary>
	/// The same state-wins-else-root cascade <see cref="Pick" /> applies to a plain field, but reading the
	/// typed <c>icon</c> shape (falling back to a legacy bare <c>iconId</c>) and returning only an
	/// icon-pack reference's bare id - <see cref="MacroDeck.Sdk.Devices.DeviceSurfaceAppearance.IconId" />
	/// stays GUID-only forever, so a reference naming any other provider resolves to no icon here exactly
	/// as an unparseable legacy id always has.
	/// </summary>
	private static string? PickIconPackReference(JsonObject? state, JsonObject root)
	{
		var reference = ReadIconReference(state) ?? ReadIconReference(root);
		return reference is { Type: WidgetIconReference.IconPackType } value ? value.Reference : null;
	}

	private static WidgetIconReference? ReadIconReference(JsonObject? appearance)
		=> WidgetIconReference.Read(appearance?["icon"], ReadString(appearance, "iconId"));

	private async Task<DeviceSurfaceWidget> BuildWidget(
		Widget widget,
		string culture,
		List<DeviceSurfaceSubscription> subscriptions,
		CancellationToken cancellationToken)
	{
		var isPersisted = Guid.TryParse(widget.Id, out var widgetId);
		var state = isPersisted ? await _widgetStates.Resolve(widgetId, cancellationToken) : null;
		var stateId = state?.StateId;

		var appearance = await BuildAppearance(widget, widgetId, isPersisted, stateId, cancellationToken);
		if (isPersisted)
		{
			subscriptions.Add(new DeviceSurfaceSubscription(widgetId, stateId ?? LabelGroups.Normalize(string.Empty)));
		}

		return new DeviceSurfaceWidget
		{
			Id = widget.Id,
			Type = widget.Type.ToString(),
			PositionX = widget.PositionX,
			PositionY = widget.PositionY,
			Width = widget.Width,
			Height = widget.Height,
			IsPinned = widget.IsPinned,
			StateId = stateId,
			StateLabel = state is null ? null : _localization.Resolve(state.StateLabel, culture),
			Appearance = appearance,
			SupportedInteractions = SupportedInteractions(widget)
		};
	}

	/// <summary>
	/// Builds the curated appearance a device renders from. The widget's stored <c>Data</c> never
	/// travels: it carries the flow definitions, the state mapping and the provider configuration, none
	/// of which a rendering provider is entitled to see.
	/// </summary>
	private async Task<DeviceSurfaceAppearance> BuildAppearance(
		Widget widget,
		Guid widgetId,
		bool isPersisted,
		string? stateId,
		CancellationToken cancellationToken)
	{
		var model = ActionButtonStateModel.Read(widget.Data);
		var root = model.Data;
		var state = model.StateMode ? model.FindState(stateId)?.Appearance : null;
		var iconDisplay = state?["iconDisplay"] as JsonObject ?? root["iconDisplay"] as JsonObject;

		var label = isPersisted
			? await _labelText.ResolveText(widgetId, stateId ?? string.Empty, cancellationToken)
			: null;

		// An active icon provider is authoritative for the whole button (issue #425 decision 1) and is
		// checked first: it decides IconId/IconVersion/HasProviderIcon together, so the three can never
		// disagree about who currently owns the icon.
		var providerIcon = isPersisted
			? await _widgetIcons.Resolve(widgetId, cancellationToken)
			: WidgetIconResolution.Inactive;

		// IconId never carries anything but an icon-pack GUID, so an active provider forces it null here
		// rather than widening it to a provider reference - see DeviceSurfaceAppearance.IconId's remarks.
		var iconPackReference = providerIcon.IsActive ? null : PickIconPackReference(state, root);
		var providerResource = providerIcon is { IsActive: true, Resource: { } resource } ? resource : null;

		return new DeviceSurfaceAppearance
		{
			Label = label ?? Pick(state, root, "label"),
			LabelColor = Pick(state, root, "labelColor"),
			BackgroundColor = Pick(state, root, "backgroundColor"),
			IconId = iconPackReference,
			// Carried so that re-rendering an icon under the same id (or the same provider) still changes
			// the projected surface: without it the push rule sees an identical surface and the device
			// keeps its stale bytes.
			IconVersion = providerResource?.ContentHash ?? IconVersionOf(iconPackReference),
			HasProviderIcon = providerResource is not null,
			IconFit = ReadString(iconDisplay, "fit"),
			IconZoom = ReadDouble(iconDisplay, "zoom"),
			IconOffsetX = ReadDouble(iconDisplay, "offsetX"),
			IconOffsetY = ReadDouble(iconDisplay, "offsetY"),
			FontSize = ReadInt(state, "fontSize") ?? ReadInt(root, "fontSize"),
			TextAlign = Pick(state, root, "textAlign"),
			LabelPosition = Pick(state, root, "labelPosition")
		};
	}

	private string? IconVersionOf(string? iconId)
	{
		if (!Guid.TryParse(iconId, out var id) || _icons.GetIconById(id) is not { } icon)
		{
			return null;
		}

		return icon.MasterContentHash ?? icon.SourceContentHash;
	}

	// A cross-profile navigation moves the session off the device's assigned profile, so the session's
	// own profile wins while it still resolves; the assignment is only the starting point.
	private string? ResolveProfileId(string? currentProfileId, string? startupProfileId)
		=> currentProfileId is not null && ResolveProfile(currentProfileId) is not null
			? currentProfileId
			: startupProfileId;

	private Ui.Transport.Messages.Profiles.Profile? ResolveProfile(string profileId)
		=> _profiles.GetProfiles()
			.FirstOrDefault(profile => string.Equals(profile.Id, profileId, StringComparison.Ordinal));
}
