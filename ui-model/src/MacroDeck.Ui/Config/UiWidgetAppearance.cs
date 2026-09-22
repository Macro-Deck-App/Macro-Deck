using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;

namespace MacroDeck.Ui.Config;

/// <summary>
/// The stored-data keys of Macro Deck's standard widget appearance. The widget appearance actions write these
/// keys for a widget type that declares the matching appearance property, and
/// <see cref="UiWidgetAppearance.Section" /> edits them. The names and value shapes are stable.
/// </summary>
public static class UiWidgetAppearanceKeys
{
	/// <summary>Background colour as <c>#rrggbb</c>.</summary>
	public const string BackgroundColor = "backgroundColor";

	/// <summary>Caption text, stored as entered. It may contain a <c>{{ ... }}</c> variable template, which
	/// Macro Deck renders only for its own Action Button.</summary>
	public const string Label = "label";

	/// <summary>Caption colour as <c>#rrggbb</c>.</summary>
	public const string LabelColor = "labelColor";

	/// <summary>Font face id from the host's font catalogue, the value of the <c>macrodeck.fonts</c> option
	/// source.</summary>
	public const string FontFaceId = "fontFaceId";

	/// <summary>Caption size as a whole-number percentage.</summary>
	public const string FontSize = "fontSize";

	/// <summary>Horizontal caption alignment: <c>left</c>, <c>center</c> or <c>right</c>.</summary>
	public const string TextAlign = "textAlign";

	/// <summary>Vertical caption position: <c>top</c>, <c>center</c> or <c>bottom</c>.</summary>
	public const string LabelPosition = "labelPosition";

	/// <summary>Highlight colour as <c>#rrggbb</c>.</summary>
	public const string AccentColor = "accentColor";

	/// <summary>The border object, <c>{ "style": ..., "color": "#rrggbb" }</c>. Macro Deck draws it around
	/// the tile for every widget type except its Action Button; <c>style</c> is <c>off</c> or one of
	/// <see cref="UiComponentBorderStyles" />, and a missing <c>color</c> draws the default ring colour.</summary>
	public const string Border = "border";

	/// <summary>The <c>style</c> member of <see cref="Border" />.</summary>
	public const string BorderStyle = "style";

	/// <summary>The <c>color</c> member of <see cref="Border" />.</summary>
	public const string BorderColor = "color";

	/// <summary>The host option source <see cref="UiWidgetAppearance.Section" />'s font field reads, the same
	/// id as <c>MacroDeck.Sdk.Widgets.WidgetOptionsSources.Fonts</c>.</summary>
	public const string FontsOptionsSource = "macrodeck.fonts";
}

/// <summary>
/// Which groups of standard fields <see cref="UiWidgetAppearance.Section" /> builds. Named after the matching
/// <c>MacroDeck.Sdk.Widgets.WidgetAppearanceProperty</c> values, which a widget type declares so the
/// appearance actions reach the same keys.
/// </summary>
[Flags]
public enum UiWidgetAppearanceFields
{
	/// <summary>No fields.</summary>
	None = 0,

	/// <summary><see cref="UiWidgetAppearanceKeys.BackgroundColor" />.</summary>
	BackgroundColor = 1,

	/// <summary><see cref="UiWidgetAppearanceKeys.Label" />.</summary>
	Label = 2,

	/// <summary><see cref="UiWidgetAppearanceKeys.LabelColor" />.</summary>
	LabelColor = 4,

	/// <summary>Font face, size, alignment and caption position.</summary>
	Font = 8,

	/// <summary><see cref="UiWidgetAppearanceKeys.AccentColor" />.</summary>
	AccentColor = 16,

	/// <summary>The border style and colour.</summary>
	Border = 32,

	/// <summary>Every field.</summary>
	All = BackgroundColor | Label | LabelColor | Font | AccentColor | Border
}

/// <summary>A widget's standard appearance values as read from its stored data. Null means not set.</summary>
public sealed record UiWidgetAppearanceValues
{
	/// <summary>See <see cref="UiWidgetAppearanceKeys.BackgroundColor" />.</summary>
	public string? BackgroundColor { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.Label" />.</summary>
	public string? Label { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.LabelColor" />.</summary>
	public string? LabelColor { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.FontFaceId" />.</summary>
	public string? FontFaceId { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.FontSize" />.</summary>
	public double? FontSize { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.TextAlign" />.</summary>
	public string? TextAlign { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.LabelPosition" />.</summary>
	public string? LabelPosition { get; init; }

	/// <summary>See <see cref="UiWidgetAppearanceKeys.AccentColor" />.</summary>
	public string? AccentColor { get; init; }

	/// <summary>The border's style. Macro Deck already draws the border; read it only to react to it.</summary>
	public string? BorderStyle { get; init; }

	/// <summary>The border's colour.</summary>
	public string? BorderColor { get; init; }
}

/// <summary>Macro Deck's standard widget appearance fields for a provider's own widget configuration.</summary>
public static class UiWidgetAppearance
{
	private const string Off = "off";

	private static readonly IReadOnlyList<string> _colorBorderStyles =
	[
		UiComponentBorderStyles.Static, UiComponentBorderStyles.Heartbeat, UiComponentBorderStyles.Breathing,
		UiComponentBorderStyles.Blink, UiComponentBorderStyles.Comet, UiComponentBorderStyles.Ants,
	];

	/// <summary>
	/// Builds the standard fields for <paramref name="fields" />, seeded from the widget's stored
	/// <paramref name="data" /> and writing the <see cref="UiWidgetAppearanceKeys" />. Place the returned fragment
	/// in <see cref="UiWidgetProperties.Children" />.
	/// </summary>
	/// <remarks>
	/// The fragment owns those keys as input ids, plus the heading keys <c>appearance-heading</c> and
	/// <c>border-heading</c>, so the rest of the tree must not use them. Saving without touching a field leaves
	/// its key as stored; an empty label or border colour is removed. The font field lists the host's fonts
	/// from the <see cref="UiWidgetAppearanceKeys.FontsOptionsSource" /> option source, which a Macro Deck release
	/// older than these fields does not resolve for a provider's tree.
	/// </remarks>
	/// <param name="data">The widget's stored configuration, from the <c>widgetData</c> surface attribute.</param>
	/// <param name="fields">Which groups to build. <see cref="UiWidgetAppearanceFields.None" /> builds an empty
	/// fragment.</param>
	/// <param name="key">The fragment's key.</param>
	public static UiFragment Section(JsonElement data, UiWidgetAppearanceFields fields, string key = "appearance")
	{
		var children = new List<UiElement>();

		if ((fields & ~UiWidgetAppearanceFields.Border) != UiWidgetAppearanceFields.None)
		{
			children.Add(new UiHeading { Key = "appearance-heading", Text = MacroDeckStrings.Widgets.Appearance.Heading() });
		}

		if (fields.HasFlag(UiWidgetAppearanceFields.Label))
		{
			children.Add(new UiStringInput
			{
				Key = UiWidgetAppearanceKeys.Label,
				Label = MacroDeckStrings.Widgets.Appearance.Label(),
				Binding = OptionalText(ReadString(data, UiWidgetAppearanceKeys.Label)),
			});
		}

		if (fields.HasFlag(UiWidgetAppearanceFields.BackgroundColor))
		{
			children.Add(Color(data, UiWidgetAppearanceKeys.BackgroundColor, MacroDeckStrings.Widgets.Appearance.BackgroundColor()));
		}

		if (fields.HasFlag(UiWidgetAppearanceFields.LabelColor))
		{
			children.Add(Color(data, UiWidgetAppearanceKeys.LabelColor, MacroDeckStrings.Widgets.Appearance.LabelColor()));
		}

		if (fields.HasFlag(UiWidgetAppearanceFields.AccentColor))
		{
			children.Add(Color(data, UiWidgetAppearanceKeys.AccentColor, MacroDeckStrings.Widgets.Appearance.AccentColor()));
		}

		if (fields.HasFlag(UiWidgetAppearanceFields.Font))
		{
			children.AddRange(FontFields(data));
		}

		if (fields.HasFlag(UiWidgetAppearanceFields.Border))
		{
			children.Add(new UiHeading { Key = "border-heading", Text = MacroDeckStrings.Widgets.Appearance.Border() });
			children.Add(BorderField(data));
		}

		return new UiFragment { Key = key, Children = children };
	}

	/// <summary>Reads the standard appearance values from a widget's stored data. A missing key, or one holding
	/// a value of the wrong kind, reads as null.</summary>
	public static UiWidgetAppearanceValues Read(JsonElement data)
	{
		var border = ReadObject(data, UiWidgetAppearanceKeys.Border);

		return new UiWidgetAppearanceValues
		{
			BackgroundColor = ReadString(data, UiWidgetAppearanceKeys.BackgroundColor),
			Label = ReadString(data, UiWidgetAppearanceKeys.Label),
			LabelColor = ReadString(data, UiWidgetAppearanceKeys.LabelColor),
			FontFaceId = ReadString(data, UiWidgetAppearanceKeys.FontFaceId),
			FontSize = ReadNumber(data, UiWidgetAppearanceKeys.FontSize),
			TextAlign = ReadString(data, UiWidgetAppearanceKeys.TextAlign),
			LabelPosition = ReadString(data, UiWidgetAppearanceKeys.LabelPosition),
			AccentColor = ReadString(data, UiWidgetAppearanceKeys.AccentColor),
			BorderStyle = ReadString(border, UiWidgetAppearanceKeys.BorderStyle),
			BorderColor = ReadString(border, UiWidgetAppearanceKeys.BorderColor),
		};
	}

	private static UiColorInput Color(JsonElement data, string key, LocalizedString label)
	{
		var state = new UiState<string>(ReadString(data, key) ?? string.Empty);

		return new UiColorInput { Key = key, Label = label, Binding = Bind.To(state), SupportsReset = true };
	}

	private static IEnumerable<UiElement> FontFields(JsonElement data)
	{
		var size = new UiState<double?>(ReadNumber(data, UiWidgetAppearanceKeys.FontSize));
		var align = new UiState<string>(ReadString(data, UiWidgetAppearanceKeys.TextAlign) ?? string.Empty);
		var position = new UiState<string>(ReadString(data, UiWidgetAppearanceKeys.LabelPosition) ?? string.Empty);

		yield return new UiDynamicChoiceInput
		{
			Key = UiWidgetAppearanceKeys.FontFaceId,
			Label = MacroDeckStrings.Widgets.Appearance.Font(),
			Binding = OptionalText(ReadString(data, UiWidgetAppearanceKeys.FontFaceId)),
			OptionsSourceId = UiWidgetAppearanceKeys.FontsOptionsSource,
			DynamicOptions = true,
		};
		yield return new UiNumberInput
		{
			Key = UiWidgetAppearanceKeys.FontSize,
			Label = MacroDeckStrings.Widgets.Appearance.FontSize(),
			Step = 1,
			Binding = UiBinding<double>.Create(
				UiValue.Optional(() => size.Value is { } value ? UiValue.Of(value) : UiValue.None<double>()),
				value => size.Value = value),
		};
		yield return new UiChoiceInput
		{
			Key = UiWidgetAppearanceKeys.TextAlign,
			Label = MacroDeckStrings.Widgets.Appearance.TextAlign(),
			Binding = Bind.To(align),
			Options = UiValue.Of<IReadOnlyList<UiOption>>([
				UiOption.Of("left", MacroDeckStrings.Widgets.Appearance.TextAlignLeft()),
				UiOption.Of("center", MacroDeckStrings.Widgets.Appearance.TextAlignCenter()),
				UiOption.Of("right", MacroDeckStrings.Widgets.Appearance.TextAlignRight()),
			]),
		};
		yield return new UiChoiceInput
		{
			Key = UiWidgetAppearanceKeys.LabelPosition,
			Label = MacroDeckStrings.Widgets.Appearance.LabelPosition(),
			Binding = Bind.To(position),
			Options = UiValue.Of<IReadOnlyList<UiOption>>([
				UiOption.Of("top", MacroDeckStrings.Widgets.Appearance.LabelPositionTop()),
				UiOption.Of("center", MacroDeckStrings.Widgets.Appearance.LabelPositionCenter()),
				UiOption.Of("bottom", MacroDeckStrings.Widgets.Appearance.LabelPositionBottom()),
			]),
		};
	}

	private static UiObjectInput BorderField(JsonElement data)
	{
		var stored = data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(UiWidgetAppearanceKeys.Border, out var node) &&
			node.ValueKind == JsonValueKind.Object
				? JsonNode.Parse(node.GetRawText()) as JsonObject
				: null;
		var style = new UiState<string>(ReadString(stored, UiWidgetAppearanceKeys.BorderStyle) ?? string.Empty);
		var color = new UiState<string>(ReadString(stored, UiWidgetAppearanceKeys.BorderColor) ?? string.Empty);

		// The object binds its whole value, which the editor's draft takes as authoritative: that keeps members
		// it does not edit and leaves an absent border absent instead of writing an empty object.
		JsonElement Compose()
		{
			if (stored is null && style.Value.Length == 0 && color.Value.Length == 0)
			{
				return JsonSerializer.SerializeToElement<object?>(null);
			}

			var next = stored?.DeepClone().AsObject() ?? [];
			Overlay(next, UiWidgetAppearanceKeys.BorderStyle, style.Value);
			Overlay(next, UiWidgetAppearanceKeys.BorderColor, color.Value);
			return JsonSerializer.SerializeToElement(next);
		}

		return new UiObjectInput
		{
			Key = UiWidgetAppearanceKeys.Border,
			Binding = Bind.Custom(Compose, value =>
			{
				style.Value = ReadString(value, UiWidgetAppearanceKeys.BorderStyle) ?? string.Empty;
				color.Value = ReadString(value, UiWidgetAppearanceKeys.BorderColor) ?? string.Empty;
			}),
			Children =
			[
				new UiChoiceInput
				{
					Key = UiWidgetAppearanceKeys.BorderStyle,
					Label = MacroDeckStrings.Widgets.Appearance.BorderStyle(),
					Binding = Bind.To(style),
					Options = UiValue.Of<IReadOnlyList<UiOption>>([
						UiOption.Of(Off, MacroDeckStrings.Widgets.Appearance.BorderOff()),
						UiOption.Of(UiComponentBorderStyles.Static, MacroDeckStrings.Widgets.Appearance.BorderStatic()),
						UiOption.Of(UiComponentBorderStyles.Heartbeat, MacroDeckStrings.Widgets.Appearance.BorderHeartbeat()),
						UiOption.Of(UiComponentBorderStyles.Breathing, MacroDeckStrings.Widgets.Appearance.BorderBreathing()),
						UiOption.Of(UiComponentBorderStyles.Blink, MacroDeckStrings.Widgets.Appearance.BorderBlink()),
						UiOption.Of(UiComponentBorderStyles.Comet, MacroDeckStrings.Widgets.Appearance.BorderComet()),
						UiOption.Of(UiComponentBorderStyles.Ants, MacroDeckStrings.Widgets.Appearance.BorderMarchingAnts()),
						UiOption.Of(UiComponentBorderStyles.HueShift, MacroDeckStrings.Widgets.Appearance.BorderHueShift()),
						UiOption.Of(UiComponentBorderStyles.Rgb, MacroDeckStrings.Widgets.Appearance.BorderRgb()),
					]),
				},
				new UiColorInput
				{
					Key = UiWidgetAppearanceKeys.BorderColor,
					Label = MacroDeckStrings.Widgets.Appearance.BorderColor(),
					Binding = Bind.To(color),
					SupportsReset = true,
					DefaultValue = string.Empty,
					VisibleWhen = new UiVisibleWhen
					{
						ParameterName = UiWidgetAppearanceKeys.BorderStyle,
						Values = _colorBorderStyles,
					},
				},
			],
		};
	}

	private static void Overlay(JsonObject target, string key, string value)
	{
		if (value.Length == 0)
		{
			target.Remove(key);
		}
		else
		{
			target[key] = value;
		}
	}

	// A JSON null is the value the editor's draft reads as "remove the key", where an empty string would be
	// stored as one.
	private static UiBinding<string> OptionalText(string? initial)
	{
		var state = new UiState<string>(initial ?? string.Empty);

		return Bind.Custom<string>(() => state.Value.Length == 0 ? null! : state.Value, value => state.Value = value ?? string.Empty);
	}

	private static string? ReadString(JsonElement data, string key)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(key, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private static string? ReadString(JsonObject? data, string key)
		=> data?[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

	private static double? ReadNumber(JsonElement data, string key)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(key, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number)
				? number
				: null;

	private static JsonElement ReadObject(JsonElement data, string key)
		=> data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty(key, out var value) &&
			value.ValueKind == JsonValueKind.Object
				? value
				: default;
}
