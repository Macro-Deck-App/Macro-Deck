using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Actions;
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
			Opacity = Clamp(ReadDouble(display, "opacity"), 100, 0, 100)
		};
	}

	private static double Clamp(double? value, double fallback, double min, double max)
	{
		return value is { } v && double.IsFinite(v) ? Math.Clamp(v, min, max) : fallback;
	}

	private static double? ReadDouble(JsonObject data, string key)
	{
		return data[key] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;
	}

	private static string? ReadString(JsonObject data, string key)
	{
		return data[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
	}
}

/// <summary>One state's or the root's icon: the typed reference and its own non-cascading framing. Never
/// falls back between states or between a state and the root (issue #291).</summary>
public sealed record ActionButtonIcon(WidgetIconReference Icon, ActionButtonIconDisplay Display, string? Color = null);

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
	{
		return data[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
	}
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

	public string? LabelBoxColor { get; init; }

	public bool LabelShadow { get; init; } = true;

	public string? LabelOutlineColor { get; init; }

	public double LabelOutlineWidth { get; init; }

	public string? LabelBoxBorderColor { get; init; }

	public double LabelBoxBorderWidth { get; init; }

	public bool HasLabelOutline => LabelOutlineColor is not null && LabelOutlineWidth > 0;

	public bool HasLabelBoxBorder => LabelBoxBorderColor is not null && LabelBoxBorderWidth > 0;

	public string? FontFace { get; init; }

	public required string TextAlign { get; init; }

	public required string LabelPosition { get; init; }

	public required double FontSizePercent { get; init; }

	public double LabelX { get; init; } = ActionButtonLabelJson.Center;

	public double LabelY { get; init; } = ActionButtonLabelJson.Center;

	public IReadOnlyList<ActionButtonResolvedLabel> ExtraLabels { get; init; } = [];

	public ActionButtonBorder? Border { get; init; }

	public ActionButtonResolvedLabel PrimaryLabel
		=> new()
		{
			Text = Label,
			Color = LabelColor,
			BoxColor = LabelBoxColor,
			Shadow = LabelShadow,
			OutlineColor = LabelOutlineColor,
			OutlineWidth = LabelOutlineWidth,
			BoxBorderColor = LabelBoxBorderColor,
			BoxBorderWidth = LabelBoxBorderWidth,
			FontFace = FontFace,
			FontSizePercent = FontSizePercent,
			X = LabelX,
			Y = LabelY
		};
}

public sealed record ActionButtonResolvedLabel
{
	public static ActionButtonResolvedLabel Empty { get; } = new();

	public string? Text { get; init; }

	public string Color { get; init; } = ActionButtonWidgetData.DefaultLabelColor;

	public string? BoxColor { get; init; }

	public bool Shadow { get; init; } = true;

	public string? OutlineColor { get; init; }

	public double OutlineWidth { get; init; }

	public string? BoxBorderColor { get; init; }

	public double BoxBorderWidth { get; init; }

	public bool HasOutline => OutlineColor is not null && OutlineWidth > 0;

	public bool HasBoxBorder => BoxBorderColor is not null && BoxBorderWidth > 0;

	public bool HasBox => BoxColor is not null || HasBoxBorder;

	public string? FontFace { get; init; }

	public double FontSizePercent { get; init; } = ActionButtonWidgetData.DefaultFontSizePercent;

	public double X { get; init; } = ActionButtonLabelJson.Center;

	public double Y { get; init; } = ActionButtonLabelJson.Center;

	public string TextAlign => ActionButtonLabelJson.TextAlignOf(X);
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
			_ => null
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
				if (flow.ValueKind == JsonValueKind.Object
					&& flow.TryGetProperty("triggerType", out var triggerType)
					&& triggerType.ValueKind == JsonValueKind.String)
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
	public const double DefaultLabelStrokeWidth = 1;
	public const double MaxLabelOutlineWidth = 3;
	public const double MaxLabelBoxBorderWidth = 5;
	public const string DefaultLabelColor = "#ffffff";
	public const double DefaultFontSizePercent = 14;

	private readonly ActionButtonStateModel _model;

	private ActionButtonWidgetData(
		ActionButtonStateModel model,
		IReadOnlySet<string> triggerTypes,
		bool hasDoublePressFlow)
	{
		_model = model;
		TriggerTypes = triggerTypes;
		_hasDoublePressFlow = hasDoublePressFlow;
	}

	private readonly bool _hasDoublePressFlow;

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

	/// <summary>Whether a short press can advance the active state on its own, as a pure read so the
	/// declared event set never depends on a live resolution.</summary>
	public bool CanAdvanceState => _model.AdvancesOnPress;

	public static ActionButtonWidgetData Parse(JsonElement data)
	{
		var root = data.ValueKind == JsonValueKind.Object
			? JsonNode.Parse(data.GetRawText()) as JsonObject ?? new JsonObject()
			: new JsonObject();

		var model = ActionButtonStateModel.Read(root);
		var triggerTypes = ActionButtonFlowTriggers.ReadTriggerTypes(model.Data);

		var hasDoublePressFlow = data.ValueKind == JsonValueKind.Object
			&& WidgetFlowsJson.HasRunnableFlow(data.GetRawText(), WidgetTriggerTypes.DoublePress);

		return new ActionButtonWidgetData(model, triggerTypes, hasDoublePressFlow);
	}

	/// <summary>The event names this button's tree declares. A pure function of the stored flows and
	/// state configuration - never of whether a flow's integration happens to be loaded right now -
	/// because the tree's declared event set must not change under a live session.</summary>
	public IReadOnlyList<string> DeclaredTriggers()
	{
		var names = new List<string>();

		if (TriggerTypes.Contains(WidgetTriggerTypes.ShortPress) || CanAdvanceState || _hasDoublePressFlow)
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

		if (_hasDoublePressFlow)
		{
			names.Add(MacroDeck.Ui.Components.UiComponentEvents.DoublePress);
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
		var borderNode = appearance?["border"] as JsonObject ?? root["border"] as JsonObject;

		var x = ActionButtonLabelJson.ReadX(appearance)
			?? ActionButtonLabelJson.ReadX(root) ?? ActionButtonLabelJson.Center;
		var y = ActionButtonLabelJson.ReadY(appearance)
			?? ActionButtonLabelJson.ReadY(root) ?? ActionButtonLabelJson.Center;

		return new ActionButtonResolvedAppearance
		{
			Label = ReadString(appearance, "label") ?? ReadString(root, "label"),
			BackgroundColor = WidgetColor.NormalizeBackground(ReadString(appearance, "backgroundColor"))
				?? WidgetColor.NormalizeBackground(ReadString(root, "backgroundColor")),
			LabelColor = WidgetColor.Normalize(ReadString(appearance, "labelColor"))
				?? WidgetColor.Normalize(ReadString(root, "labelColor")) ?? DefaultLabelColor,
			LabelBoxColor = WidgetColor.Normalize(ReadString(appearance, "labelBoxColor"))
				?? WidgetColor.Normalize(ReadString(root, "labelBoxColor")),
			LabelShadow = ReadBool(appearance, "labelShadow") ?? ReadBool(root, "labelShadow") ?? true,
			LabelOutlineColor = WidgetColor.Normalize(ReadString(appearance, "labelOutlineColor"))
				?? WidgetColor.Normalize(ReadString(root, "labelOutlineColor")),
			LabelOutlineWidth
				= LabelStrokeWidth(ReadDouble(appearance, "labelOutlineWidth") ?? ReadDouble(root, "labelOutlineWidth"),
					MaxLabelOutlineWidth),
			LabelBoxBorderColor = WidgetColor.Normalize(ReadString(appearance, "labelBoxBorderColor"))
				?? WidgetColor.Normalize(ReadString(root, "labelBoxBorderColor")),
			LabelBoxBorderWidth
				= LabelStrokeWidth(ReadDouble(appearance, "labelBoxBorderWidth")
					?? ReadDouble(root, "labelBoxBorderWidth"),
					MaxLabelBoxBorderWidth),
			FontFace = Trimmed(ReadString(appearance, "fontFaceId")) ?? Trimmed(ReadString(root, "fontFaceId")),
			TextAlign = ActionButtonLabelJson.TextAlignOf(x),
			LabelPosition = ActionButtonLabelJson.LabelPositionOf(y),
			FontSizePercent = ReadDouble(appearance, "fontSize")
				?? ReadDouble(root, "fontSize") ?? DefaultFontSizePercent,
			LabelX = x,
			LabelY = y,
			ExtraLabels = ActionButtonLabelJson.ActiveExtraLabels(_model, stateId).Select(ResolveExtraLabel).ToList(),
			Border = ActionButtonBorder.Resolve(borderNode)
		};
	}

	public int ExtraLabelSlots => ActionButtonLabelJson.ExtraLabelSlots(_model);

	public bool UsesLabelLayer
	{
		get
		{
			if (ExtraLabelSlots > 0)
			{
				return true;
			}

			return StateMode
				? States.Any(state => IsOffPreset(Resolve(state.Id)))
				: IsOffPreset(Resolve(null));
		}
	}

	private static bool IsOffPreset(ActionButtonResolvedAppearance appearance)
	{
		return !ActionButtonLabelJson.IsPreset(appearance.LabelX) || !ActionButtonLabelJson.IsPreset(appearance.LabelY);
	}

	private static ActionButtonResolvedLabel ResolveExtraLabel(JsonObject label)
	{
		return new ActionButtonResolvedLabel
		{
			Text = ReadString(label, "label"),
			Color = WidgetColor.Normalize(ReadString(label, "labelColor")) ?? DefaultLabelColor,
			BoxColor = WidgetColor.Normalize(ReadString(label, "labelBoxColor")),
			Shadow = ReadBool(label, "labelShadow") ?? true,
			OutlineColor = WidgetColor.Normalize(ReadString(label, "labelOutlineColor")),
			OutlineWidth = LabelStrokeWidth(ReadDouble(label, "labelOutlineWidth"), MaxLabelOutlineWidth),
			BoxBorderColor = WidgetColor.Normalize(ReadString(label, "labelBoxBorderColor")),
			BoxBorderWidth = LabelStrokeWidth(ReadDouble(label, "labelBoxBorderWidth"), MaxLabelBoxBorderWidth),
			FontFace = Trimmed(ReadString(label, "fontFaceId")),
			FontSizePercent = ReadDouble(label, "fontSize") ?? DefaultFontSizePercent,
			X = ActionButtonLabelJson.ReadX(label) ?? ActionButtonLabelJson.Center,
			Y = ActionButtonLabelJson.ReadY(label) ?? ActionButtonLabelJson.Center
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
					ActionButtonIconDisplay.Parse(appearance?["iconDisplay"] as JsonObject),
					WidgetColor.Normalize(ReadString(appearance, "iconColor")));
		}

		var root = _model.Data;
		var rootReference = ReadIcon(root);
		var rootDisplay = root["iconDisplay"] as JsonObject;

		if (rootReference is not null || rootDisplay is not null)
		{
			return rootReference is null
				? null
				: new ActionButtonIcon(rootReference.Value,
					ActionButtonIconDisplay.Parse(rootDisplay),
					WidgetColor.Normalize(ReadString(root, "iconColor")));
		}

		// A legacy tail: a bag saved before the icon/iconDisplay fields moved to the root still carries
		// them on states[0] - see icon-display.util.ts's momentaryIcon for the client-side precedent.
		var legacy = States.Count > 0 ? States[0].Appearance : null;
		var legacyReference = ReadIcon(legacy);

		return legacyReference is null
			? null
			: new ActionButtonIcon(legacyReference.Value,
				ActionButtonIconDisplay.Parse(legacy?["iconDisplay"] as JsonObject),
				WidgetColor.Normalize(ReadString(legacy, "iconColor")));
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
	{
		return WidgetIconReference.Read(appearance?["icon"], Trimmed(ReadString(appearance, "iconId")));
	}

	/// <summary>The legacy <c>imageUrl</c>/<c>imageId</c> field, only meaningful when
	/// <see cref="StateMode" /> is off and drawn only as a last resort when no icon resolves - see
	/// <see cref="ActionButtonImageResource" /> for how a data URI becomes a resource.</summary>
	public string? ImageUrl => StateMode ? null : Trimmed(ReadString(_model.Data, "imageUrl"));

	private static string? Trimmed(string? value)
	{
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	private static double LabelStrokeWidth(double? value, double max)
	{
		return value is { } v && double.IsFinite(v) ? Math.Clamp(v, 0, max) : DefaultLabelStrokeWidth;
	}

	private static bool? ReadBool(JsonObject? data, string key)
	{
		return data?[key] is JsonValue value && value.TryGetValue<bool>(out var b) ? b : null;
	}

	private static string? ReadString(JsonObject? data, string key)
	{
		return data?[key] is JsonValue value && value.TryGetValue<string>(out var s) ? s : null;
	}

	private static double? ReadDouble(JsonObject? data, string key)
	{
		return data?[key] is JsonValue value && value.TryGetValue<double>(out var d) ? d : null;
	}
}
