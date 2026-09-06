using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Widgets;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>Non-cascading, clamped icon framing - the fraction-space form of the stored percentages,
/// mirroring <c>icon-display.util.ts</c>'s <c>resolveIconDisplay</c>.</summary>
public sealed record ActionButtonIconDisplay
{
	public const string Contain = "contain";

	public const string Cover = "cover";

	public string Fit { get; init; } = Contain;

	public double Zoom { get; init; } = 100;

	public double OffsetX { get; init; }

	public double OffsetY { get; init; }

	public double Opacity { get; init; } = 100;

	public static ActionButtonIconDisplay Default { get; } = new();

	/// <summary>Whether this framing differs from the identity framing - the effective-value test that
	/// decides whether the button emits any framing keys at all.</summary>
	public bool IsDefault
		=> Fit == Contain && Zoom == 100 && OffsetX == 0 && OffsetY == 0 && Opacity == 100;

	public static ActionButtonIconDisplay Parse(JsonObject? display)
	{
		if (display is null)
		{
			return Default;
		}

		return new ActionButtonIconDisplay
		{
			Fit = ReadString(display, "fit") == Cover ? Cover : Contain,
			Zoom = Clamp(ReadDouble(display, "zoom"), 100, 10, 400),
			OffsetX = Clamp(ReadDouble(display, "offsetX"), 0, -100, 100),
			OffsetY = Clamp(ReadDouble(display, "offsetY"), 0, -100, 100),
			Opacity = Clamp(ReadDouble(display, "opacity"), 100, 0, 100),
		};
	}

	private static double Clamp(double? value, double fallback, double min, double max)
		=> value is { } v && double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;

	private static double? ReadDouble(JsonObject data, string key)
		=> data[key] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;

	private static string? ReadString(JsonObject data, string key)
		=> data[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>One state's or the root's icon: the typed reference and its own non-cascading framing. Never
/// falls back between states or between a state and the root (issue #291).</summary>
public sealed record ActionButtonIcon(WidgetIconReference Icon, ActionButtonIconDisplay Display);

/// <summary>A resolved border ring, or <c>null</c> when none is configured for the active appearance.
/// <see cref="Color" /> is only ever populated for the tinted styles - <c>hue-shift</c> and <c>rgb</c>
/// ignore color entirely, matching the profile's <see cref="MacroDeck.Ui.Components.UiComponentBorderStyles" />
/// doc.</summary>
public sealed record ActionButtonBorder(string Style, string? Color)
{
	private static readonly HashSet<string> _tinted =
		new(StringComparer.Ordinal) { "static", "heartbeat", "breathing", "blink", "comet", "ants" };

	public static ActionButtonBorder? Resolve(JsonObject? border)
	{
		if (border is null)
		{
			return null;
		}

		var style = ReadString(border, "style");
		if (string.IsNullOrEmpty(style) || style == "off")
		{
			return null;
		}

		if (!_tinted.Contains(style))
		{
			return new ActionButtonBorder(style, null);
		}

		var color = WidgetColor.Normalize(ReadString(border, "color")) ?? "#ffffff";
		return new ActionButtonBorder(style, color);
	}

	private static string? ReadString(JsonObject data, string key)
		=> data[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
}

/// <summary>One state's or the whole button's resolved appearance, with every field-level cascade
/// (state field -> root field -> default) already applied exactly as
/// <c>widget-appearance.util.ts</c>'s <c>resolveButtonState</c> applies it. Icon and border are resolved
/// separately - see <see cref="ActionButtonWidgetData.ResolveIcon" /> and the non-cascading rule it
/// documents.</summary>
public sealed record ActionButtonResolvedAppearance
{
	public string? Label { get; init; }

	public string? BackgroundColor { get; init; }

	public required string LabelColor { get; init; }

	public string? FontFace { get; init; }

	public required string TextAlign { get; init; }

	public required string LabelPosition { get; init; }

	public required double FontSizePercent { get; init; }

	public ActionButtonBorder? Border { get; init; }
}

/// <summary>Which action-button trigger names are wired to a flow, read straight off the stored
/// <c>flows</c> array without deserializing the flow bodies - <see cref="ActionButtonWidgetData.DeclaredTriggers" />
/// is the only thing that needs them.</summary>
internal static class ActionButtonFlowTriggers
{
	public static IReadOnlySet<string> ReadTriggerTypes(JsonObject data)
	{
		if (data["flows"] is not { } flowsNode)
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		var flowsJson = flowsNode switch
		{
			JsonValue value when value.TryGetValue<string>(out var text) => text,
			JsonArray array => array.ToJsonString(),
			_ => null,
		};

		if (string.IsNullOrWhiteSpace(flowsJson))
		{
			return new HashSet<string>(StringComparer.Ordinal);
		}

		var result = new HashSet<string>(StringComparer.Ordinal);

		try
		{
			using var document = JsonDocument.Parse(flowsJson);
			if (document.RootElement.ValueKind != JsonValueKind.Array)
			{
				return result;
			}

			foreach (var flow in document.RootElement.EnumerateArray())
			{
				if (flow.ValueKind == JsonValueKind.Object &&
					flow.TryGetProperty("triggerType", out var triggerType) &&
					triggerType.ValueKind == JsonValueKind.String)
				{
					var value = triggerType.GetString();
					if (!string.IsNullOrEmpty(value))
					{
						result.Add(value);
					}
				}
			}
		}
		catch (JsonException)
		{
			// Malformed stored flows draw a button with no declared triggers rather than fault the tree.
		}

		return result;
	}
}

/// <summary>
/// The parsed, upgraded Action Button widget data: the root appearance fallback, every state, and which
/// triggers the stored flows declare. Parses <b>through</b> <see cref="ActionButtonStateModel.Read" />,
/// never re-deriving the legacy-upgrade shape.
/// </summary>
public sealed class ActionButtonWidgetData
{
	private readonly ActionButtonStateModel _model;

	private ActionButtonWidgetData(ActionButtonStateModel model, IReadOnlySet<string> triggerTypes)
	{
		_model = model;
		TriggerTypes = triggerTypes;
	}

	public bool StateMode => _model.StateMode;

	public IReadOnlyList<ActionButtonStateEntry> States => _model.States;

	private IReadOnlySet<string> TriggerTypes { get; }

	/// <summary>The state a session opens on: the stored explicit choice, falling back to the first
	/// declared state. <c>null</c> when the button is not in state mode or declares no states.</summary>
	public string? InitialStateId
		=> StateMode ? _model.ActiveStateId ?? (States.Count > 0 ? States[0].Id : null) : null;

	/// <summary>The literally stored active-state id, with no first-state fallback - unlike
	/// <see cref="InitialStateId" />, which exists so the tree always has something to render.
	/// <c>null</c> exactly when nothing has ever been explicitly chosen, matching
	/// <c>ActionButtonStateJson.NextStateId</c>'s own "no current" case.</summary>
	public string? StoredActiveStateId => StateMode ? _model.ActiveStateId : null;

	/// <summary>Whether a short press can advance the active state on its own: the button has not turned
	/// cycling off (<c>cycleStatesOnPress</c>) and nothing else is authoritative for the state - the same
	/// condition <c>WidgetTriggerService</c> enforces before writing, restated here as a pure read so the
	/// declared event set never depends on a live resolution.</summary>
	public bool CanAdvanceState
		=> StateMode &&
			_model.CycleStatesOnPress &&
			_model.StateProvider is null &&
			_model.StateMapping is null &&
			States.Count > 0;

	public static ActionButtonWidgetData Parse(JsonElement data)
	{
		var root = data.ValueKind == JsonValueKind.Object
			? JsonNode.Parse(data.GetRawText()) as JsonObject ?? new JsonObject()
			: new JsonObject();

		var model = ActionButtonStateModel.Read(root);
		var triggerTypes = ActionButtonFlowTriggers.ReadTriggerTypes(model.Data);

		return new ActionButtonWidgetData(model, triggerTypes);
	}

	/// <summary>The event names this button's tree declares. A pure function of the stored flows and
	/// state configuration - never of whether a flow's integration happens to be loaded right now -
	/// because the tree's declared event set must not change under a live session.</summary>
	public IReadOnlyList<string> DeclaredTriggers()
	{
		var names = new List<string>();

		if (TriggerTypes.Contains(WidgetTriggerTypes.ShortPress) || CanAdvanceState)
		{
			names.Add(MacroDeck.Ui.Components.UiComponentEvents.Press);
		}

		if (TriggerTypes.Contains(WidgetTriggerTypes.LongPress))
		{
			names.Add(MacroDeck.Ui.Components.UiComponentEvents.LongPress);
		}

		if (TriggerTypes.Contains(WidgetTriggerTypes.TouchStart))
		{
			names.Add(MacroDeck.Ui.Components.UiComponentEvents.PressStart);
		}

		if (TriggerTypes.Contains(WidgetTriggerTypes.TouchEnd))
		{
			names.Add(MacroDeck.Ui.Components.UiComponentEvents.PressEnd);
		}

		return names;
	}

	/// <summary>Resolves the appearance for <paramref name="stateId" /> (or the root appearance when
	/// not in state mode / the id does not match a known state), applying the state field -> root
	/// field -> default cascade exactly as <c>resolveButtonState</c> does. The fallback background is
	/// always absent here - a button's "no background configured" meaning (the reader's own accent) is
	/// a tree-level default, not something this resolution should bake in.</summary>
	public ActionButtonResolvedAppearance Resolve(string? stateId)
	{
		var state = StateMode ? _model.FindState(stateId) : null;
		var appearance = state?.Appearance;
		var root = _model.Data;

		// Whole-object fallback, matching `state?.border ?? data.border`: a state that carries its own
		// border object (even one whose style is explicitly "off") never falls back to root, and one
		// that carries none does.
		var borderNode = (appearance?["border"] as JsonObject) ?? (root["border"] as JsonObject);

		return new ActionButtonResolvedAppearance
		{
			Label = ReadString(appearance, "label") ?? ReadString(root, "label"),
			BackgroundColor = WidgetColor.Normalize(ReadString(appearance, "backgroundColor")) ??
				WidgetColor.Normalize(ReadString(root, "backgroundColor")),
			LabelColor = WidgetColor.Normalize(ReadString(appearance, "labelColor")) ??
				WidgetColor.Normalize(ReadString(root, "labelColor")) ?? "#ffffff",
			FontFace = Trimmed(ReadString(appearance, "fontFaceId")) ?? Trimmed(ReadString(root, "fontFaceId")),
			TextAlign = ReadString(appearance, "textAlign") ?? ReadString(root, "textAlign") ?? "center",
			LabelPosition = ReadString(appearance, "labelPosition") ?? ReadString(root, "labelPosition") ?? "center",
			FontSizePercent = ReadDouble(appearance, "fontSize") ?? ReadDouble(root, "fontSize") ?? 14,
			Border = ActionButtonBorder.Resolve(borderNode),
		};
	}

	/// <summary>The icon for <paramref name="stateId" /> (or the root icon when not in state mode),
	/// with its own framing - never falling back to another state's or the root's icon (issue #291).
	/// <c>null</c> when that appearance configures no icon at all. Reads the typed <c>icon</c> shape
	/// first and a legacy bare <c>iconId</c> second, tolerant forever regardless of whether this widget
	/// has ever passed through <c>WidgetIconReferenceMigration</c>.</summary>
	public ActionButtonIcon? ResolveIcon(string? stateId)
	{
		if (StateMode)
		{
			var appearance = _model.FindState(stateId)?.Appearance;
			var reference = ReadIcon(appearance);

			return reference is null
				? null
				: new ActionButtonIcon(reference.Value,
					ActionButtonIconDisplay.Parse(appearance?["iconDisplay"] as JsonObject));
		}

		var root = _model.Data;
		var rootReference = ReadIcon(root);
		var rootDisplay = root["iconDisplay"] as JsonObject;

		if (rootReference is not null || rootDisplay is not null)
		{
			return rootReference is null
				? null
				: new ActionButtonIcon(rootReference.Value, ActionButtonIconDisplay.Parse(rootDisplay));
		}

		// A legacy tail: a bag saved before the icon/iconDisplay fields moved to the root still carries
		// them on states[0] - see icon-display.util.ts's momentaryIcon for the client-side precedent.
		var legacy = States.Count > 0 ? States[0].Appearance : null;
		var legacyReference = ReadIcon(legacy);

		return legacyReference is null
			? null
			: new ActionButtonIcon(legacyReference.Value,
				ActionButtonIconDisplay.Parse(legacy?["iconDisplay"] as JsonObject));
	}

	/// <summary>Every distinct icon reference used anywhere in this button's data, so a session can
	/// resolve them all up front rather than during a synchronous view build.</summary>
	public IReadOnlyCollection<WidgetIconReference> AllIconReferences()
	{
		var references = new HashSet<WidgetIconReference>();

		if (StateMode)
		{
			foreach (var state in States)
			{
				if (ReadIcon(state.Appearance) is { } reference)
				{
					references.Add(reference);
				}
			}
		}
		else if (ResolveIcon(null) is { } icon)
		{
			references.Add(icon.Icon);
		}

		return references;
	}

	private static WidgetIconReference? ReadIcon(JsonObject? appearance)
		=> WidgetIconReference.Read(appearance?["icon"], Trimmed(ReadString(appearance, "iconId")));

	/// <summary>The legacy <c>imageUrl</c>/<c>imageId</c> field, only meaningful when
	/// <see cref="StateMode" /> is off and drawn only as a last resort when no icon resolves - see
	/// <see cref="ActionButtonImageResource" /> for how a data URI becomes a resource.</summary>
	public string? ImageUrl => StateMode ? null : Trimmed(ReadString(_model.Data, "imageUrl"));

	private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

	private static string? ReadString(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;

	private static double? ReadDouble(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;
}
