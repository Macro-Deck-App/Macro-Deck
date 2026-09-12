using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Components;

/// <summary>
/// Modifies exactly one <see cref="Child" />, in one of two shapes decided by which members are assigned -
/// never by the values they evaluate to.
///
/// <para>
/// <b>Only node modifiers assigned</b> (<see cref="Background" />, <see cref="Radius" />,
/// <see cref="BorderWidth" />, <see cref="BorderColor" />, <see cref="BorderLine" />,
/// <see cref="AccessibilityLabel" />, <see cref="AccessibilityHint" />, <see cref="Disabled" />): no node is
/// emitted. The members become the child node's <see cref="UiComponentProperties.Modifiers" /> object and the
/// handlers on <see cref="UiElement.Events" /> join the child's own; the child keeps its id. Nested modifiers
/// of this shape merge outward onto the same node, and assigning one member twice on one node is a
/// <see cref="UiViewException" />. The child must be a single element: a <see cref="UiWhen" />, a
/// <see cref="UiRepeat{TItem}" /> or a <see cref="UiFragment" /> is rejected, and so are
/// <see cref="UiComponentContainer.MainSize" />, <see cref="UiComponentContainer.Fill" />,
/// <see cref="UiComponentContainer.Answer" />, <see cref="UiComponentContainer.ColumnSpan" />,
/// <see cref="UiComponentContainer.RowSpan" />, a <see cref="UiElement.Fallback" /> or a component version on
/// the modifier itself, since there is no node to carry them.
/// </para>
///
/// <para>
/// <b>Any wrapper member assigned</b> (<see cref="Padding" />, <see cref="Opacity" />, <see cref="Clip" />,
/// <see cref="Mask" />, <see cref="Frame" />): a <see cref="UiComponents.Modifier" /> node is emitted with its
/// own id from <see cref="UiElement.Key" />, carrying the wrapper properties, the node modifiers as its own
/// <see cref="UiComponentProperties.Modifiers" /> and the handlers. Its child must not set
/// <see cref="UiComponentContainer.MainSize" />, <see cref="UiComponentContainer.Fill" />,
/// <see cref="UiComponentContainer.ColumnSpan" /> or <see cref="UiComponentContainer.RowSpan" />, since a
/// parent reads those from its direct child; set them on the wrapper instead. A reader that does not know the type draws <see cref="UiElement.Fallback" />, and no fallback is
/// invented when none is given.
/// </para>
///
/// <para>
/// <b>Disabled</b> removes every declared event from the child subtree and from any fallback in it, and
/// <see cref="Runtime.UiView.Dispatch" /> ignores events aimed anywhere inside it, a bound input's change
/// included. A bound value re-emits the events when it flips back.
/// </para>
/// </summary>
public sealed record UiModifier : UiComponentContainer
{
	/// <summary>The one element this modifier applies to.</summary>
	public required UiElement Child
	{
		get;
		init => field = value ?? throw new ArgumentNullException(nameof(value));
	}

	/// <summary>The node's background, a colour or a gradient. Absent leaves the component's own.</summary>
	public UiBackgroundValue Background { get; init; }

	/// <summary>The corner radius. Does not clip; use <see cref="Clip" /> for that.</summary>
	public UiSize Radius { get; init; }

	/// <summary>The border width, drawn inside the edge without taking space. Absent means no border.</summary>
	public UiSize BorderWidth { get; init; }

	/// <summary>The border colour, as <c>#rrggbb</c>.</summary>
	public UiValue<string> BorderColor { get; init; }

	/// <summary>The border line style - see <see cref="UiComponentBorderLines" />. Absent means solid.</summary>
	public UiValue<string> BorderLine { get; init; }

	/// <summary>The name assistive technology announces for the node.</summary>
	public UiText AccessibilityLabel { get; init; }

	/// <summary>The longer description assistive technology may announce after the label.</summary>
	public UiText AccessibilityHint { get; init; }

	/// <summary>Whether the child and everything inside it is disabled. Written to the wire only while
	/// <c>true</c>.</summary>
	public UiValue<bool> Disabled { get; init; }

	/// <summary>Space between the wrapper's edge and its child, on every edge. A wrapper member.</summary>
	public UiSize Padding { get; init; }

	/// <summary>The wrapper's opacity in <c>0..1</c>, applied to the child as one picture. A wrapper
	/// member.</summary>
	public UiValue<double> Opacity { get; init; }

	/// <summary>The shape the child is clipped to - see <see cref="UiComponentClips" />. A wrapper
	/// member.</summary>
	public UiValue<string> Clip { get; init; }

	/// <summary>An opacity gradient over the child. A wrapper member.</summary>
	public UiValue<UiMask> Mask { get; init; }

	/// <summary>Size constraints on the wrapper. A wrapper member.</summary>
	public UiValue<UiFrame> Frame { get; init; }

	/// <inheritdoc />
	public override string Type => UiComponents.Modifier;

	internal bool IsWrapper
		=> Padding.IsDeclared || Opacity.IsDeclared || Clip.IsDeclared || Mask.IsDeclared || Frame.IsDeclared;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiComponentProperties.Padding, Padding.Value);
		properties.Set(UiComponentProperties.Opacity, Opacity);
		properties.Set(UiComponentProperties.Clip, Clip);
		properties.Set(UiComponentProperties.Mask, Mask);
		properties.Set(UiComponentProperties.Frame, Frame);
	}

	internal void CollectSafeMembers(List<UiModifierMember> into)
	{
		Add(into, UiComponentModifiers.Background, Background.Value);
		Add(into, UiComponentModifiers.Radius, Radius.Value);
		Add(into, UiComponentModifiers.BorderWidth, BorderWidth.Value);
		Add(into, UiComponentModifiers.BorderColor, BorderColor);
		Add(into, UiComponentModifiers.BorderLine, BorderLine);
		Add(into, UiComponentModifiers.AccessibilityLabel, AccessibilityLabel.Value);
		Add(into, UiComponentModifiers.AccessibilityHint, AccessibilityHint.Value);

		if (Disabled.IsDeclared)
		{
			into.Add(new UiModifierMember(UiComponentModifiers.Disabled,
				() => Disabled.TryEvaluate(out var disabled) && disabled ? UiCanonicalJson.ToElement(true) : null));
		}
	}

	private static void Add<T>(List<UiModifierMember> into, string key, UiValue<T> value)
	{
		if (value.IsDeclared)
		{
			into.Add(new UiModifierMember(key,
				() => value.TryEvaluate(out var current) ? UiCanonicalJson.ToElement(current) : null));
		}
	}
}

internal readonly record struct UiModifierMember(string Key, Func<JsonElement?> Evaluate);

/// <summary>
/// A node background: a literal colour, written as the bare string <c>#rrggbb</c>, or a
/// <see cref="UiGradient" />, written as its object. A string converts implicitly.
/// </summary>
[JsonConverter(typeof(UiBackgroundJsonConverter))]
public sealed record UiBackground
{
	private UiBackground(string? color, UiGradient? gradient)
	{
		Color = color;
		Gradient = gradient;
	}

	/// <summary>The literal colour, or <c>null</c> for a gradient.</summary>
	public string? Color { get; }

	/// <summary>The gradient, or <c>null</c> for a literal colour.</summary>
	public UiGradient? Gradient { get; }

	/// <summary>A literal colour, as <c>#rrggbb</c>. The implicit conversions from a string go through
	/// here.</summary>
	/// <exception cref="ArgumentException"><paramref name="color" /> is not written <c>#rrggbb</c>.</exception>
	public static UiBackground Solid(string color)
		=> new(UiModifierValidation.HexColor(color, nameof(color)), null);

	/// <summary>A gradient.</summary>
	public static UiBackground Of(UiGradient gradient)
	{
		ArgumentNullException.ThrowIfNull(gradient);

		return new UiBackground(null, gradient);
	}

	/// <summary>Reads as <see cref="Solid" />.</summary>
	public static implicit operator UiBackground(string color) => Solid(color);

	/// <summary>Reads as <see cref="Of" />.</summary>
	public static implicit operator UiBackground(UiGradient gradient) => Of(gradient);
}

/// <summary>
/// A background-valued element property: a colour, a gradient, a computed one, or absent.
/// <c>default(UiBackgroundValue)</c> is the absent value.
/// </summary>
/// <remarks>
/// Its own type rather than <c>UiValue&lt;UiBackground&gt;</c> for the reason <see cref="UiSize" /> is: C#
/// applies at most one user-defined conversion, so <c>Background = "#336699"</c> could not travel from
/// <c>string</c> through <see cref="UiBackground" /> to <c>UiValue&lt;UiBackground&gt;</c>.
/// </remarks>
public readonly record struct UiBackgroundValue
{
	private UiBackgroundValue(UiValue<UiBackground> value) => Value = value;

	internal UiValue<UiBackground> Value { get; }

	/// <summary>Whether this property was set at all.</summary>
	public bool IsDeclared => Value.IsDeclared;

	/// <summary>A literal colour, as <c>#rrggbb</c>.</summary>
	public static implicit operator UiBackgroundValue(string color) => new(UiValue.Of(UiBackground.Solid(color)));

	/// <summary>An already-built background.</summary>
	public static implicit operator UiBackgroundValue(UiBackground background) => new(UiValue.Of(background));

	/// <summary>A gradient.</summary>
	public static implicit operator UiBackgroundValue(UiGradient gradient)
		=> new(UiValue.Of(UiBackground.Of(gradient)));

	/// <summary>An already-wrapped value, so computed and optional values assign directly.</summary>
	public static implicit operator UiBackgroundValue(UiValue<UiBackground> value) => new(value);

	/// <summary>A background computed on each evaluation.</summary>
	public static UiBackgroundValue From(Func<UiBackground> compute)
	{
		ArgumentNullException.ThrowIfNull(compute);

		return new UiBackgroundValue(UiValue.From(compute));
	}

	/// <summary>The absent value.</summary>
	public static UiBackgroundValue None() => default;
}

/// <summary>
/// A colour gradient, written <c>{"linear":{"angle","stops"}}</c> or
/// <c>{"radial":{"centerX","centerY","stops"}}</c>. The angle follows CSS: <c>0</c> points towards the top and
/// it turns clockwise, in degrees.
/// </summary>
[JsonConverter(typeof(UiGradientJsonConverter))]
public sealed record UiGradient
{
	private UiGradient(bool isRadial, double angle, double centerX, double centerY, IReadOnlyList<UiGradientStop> stops)
	{
		IsRadial = isRadial;
		Angle = angle;
		CenterX = centerX;
		CenterY = centerY;
		Stops = stops;
	}

	/// <summary>Whether this is a radial gradient rather than a linear one.</summary>
	public bool IsRadial { get; }

	/// <summary>A linear gradient's direction in degrees. <c>0</c> for a radial one.</summary>
	public double Angle { get; }

	/// <summary>A radial gradient's centre across the box, in <c>0..1</c>. <c>0</c> for a linear one.</summary>
	public double CenterX { get; }

	/// <summary>A radial gradient's centre down the box, in <c>0..1</c>. <c>0</c> for a linear one.</summary>
	public double CenterY { get; }

	/// <summary>The colour stops, in order. Always at least two.</summary>
	public IReadOnlyList<UiGradientStop> Stops { get; }

	/// <summary>A linear gradient along <paramref name="angle" /> degrees.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="angle" /> is not finite.</exception>
	/// <exception cref="ArgumentException"><paramref name="stops" /> holds fewer than two stops; a reader draws nothing for fewer.</exception>
	public static UiGradient Linear(double angle, params IReadOnlyList<UiGradientStop> stops)
		=> new(false, UiModifierValidation.Finite(angle, nameof(angle)), 0, 0, UiModifierValidation.Stops(stops));

	/// <summary>A radial gradient around a centre given as fractions of the box.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A centre coordinate is outside <c>0..1</c>.</exception>
	/// <exception cref="ArgumentException"><paramref name="stops" /> holds fewer than two stops; a reader draws nothing for fewer.</exception>
	public static UiGradient Radial(double centerX, double centerY, params IReadOnlyList<UiGradientStop> stops)
		=> new(true,
			0,
			UiModifierValidation.Fraction(centerX, nameof(centerX)),
			UiModifierValidation.Fraction(centerY, nameof(centerY)),
			UiModifierValidation.Stops(stops));
}

/// <summary>One colour stop of a <see cref="UiGradient" />.</summary>
public sealed record UiGradientStop
{
	/// <summary>Where the stop sits along the gradient, in <c>0..1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is outside <c>0..1</c>.</exception>
	[JsonPropertyOrder(0)]
	public required double Offset
	{
		get;
		init => field = UiModifierValidation.Fraction(value, nameof(Offset));
	}

	/// <summary>The colour at the stop, as <c>#rrggbb</c>.</summary>
	/// <exception cref="ArgumentException">The value is not written <c>#rrggbb</c>.</exception>
	[JsonPropertyOrder(1)]
	public required string Color
	{
		get;
		init => field = UiModifierValidation.HexColor(value, nameof(Color));
	}
}

/// <summary>
/// An opacity gradient a <see cref="UiComponents.Modifier" /> lays over its child, shaped like a
/// <see cref="UiGradient" /> but with opacity stops: <c>{"linear":{"angle","stops":[{"offset","opacity"}]}}</c>
/// or <c>{"radial":{"centerX","centerY","stops":[...]}}</c>.
/// </summary>
[JsonConverter(typeof(UiMaskJsonConverter))]
public sealed record UiMask
{
	private UiMask(bool isRadial, double angle, double centerX, double centerY, IReadOnlyList<UiMaskStop> stops)
	{
		IsRadial = isRadial;
		Angle = angle;
		CenterX = centerX;
		CenterY = centerY;
		Stops = stops;
	}

	/// <summary>Whether this is a radial mask rather than a linear one.</summary>
	public bool IsRadial { get; }

	/// <summary>A linear mask's direction in degrees, as <see cref="UiGradient.Angle" />. <c>0</c> for a
	/// radial one.</summary>
	public double Angle { get; }

	/// <summary>A radial mask's centre across the box, in <c>0..1</c>.</summary>
	public double CenterX { get; }

	/// <summary>A radial mask's centre down the box, in <c>0..1</c>.</summary>
	public double CenterY { get; }

	/// <summary>The opacity stops, in order. Always at least two.</summary>
	public IReadOnlyList<UiMaskStop> Stops { get; }

	/// <summary>A linear mask along <paramref name="angle" /> degrees.</summary>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="angle" /> is not finite.</exception>
	/// <exception cref="ArgumentException"><paramref name="stops" /> holds fewer than two stops; a reader draws nothing for fewer.</exception>
	public static UiMask Linear(double angle, params IReadOnlyList<UiMaskStop> stops)
		=> new(false, UiModifierValidation.Finite(angle, nameof(angle)), 0, 0, UiModifierValidation.Stops(stops));

	/// <summary>A radial mask around a centre given as fractions of the box.</summary>
	/// <exception cref="ArgumentOutOfRangeException">A centre coordinate is outside <c>0..1</c>.</exception>
	/// <exception cref="ArgumentException"><paramref name="stops" /> holds fewer than two stops; a reader draws nothing for fewer.</exception>
	public static UiMask Radial(double centerX, double centerY, params IReadOnlyList<UiMaskStop> stops)
		=> new(true,
			0,
			UiModifierValidation.Fraction(centerX, nameof(centerX)),
			UiModifierValidation.Fraction(centerY, nameof(centerY)),
			UiModifierValidation.Stops(stops));
}

/// <summary>One opacity stop of a <see cref="UiMask" />.</summary>
public sealed record UiMaskStop
{
	/// <summary>Where the stop sits along the mask, in <c>0..1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is outside <c>0..1</c>.</exception>
	[JsonPropertyOrder(0)]
	public required double Offset
	{
		get;
		init => field = UiModifierValidation.Fraction(value, nameof(Offset));
	}

	/// <summary>How much of the child shows through at the stop, in <c>0..1</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The value is outside <c>0..1</c>.</exception>
	[JsonPropertyOrder(1)]
	public required double Opacity
	{
		get;
		init => field = UiModifierValidation.Fraction(value, nameof(Opacity));
	}
}

/// <summary>
/// Size constraints on a <see cref="UiComponents.Modifier" />, every member optional and omitted when absent.
/// A fixed <see cref="Width" /> or <see cref="Height" /> wins over the box the parent offers; the minimums and
/// maximums then clamp; <see cref="AspectRatio" /> (width over height) fits the largest box of that shape
/// inside what remains. Lengths are fractions of the widget basis, as everywhere in this profile.
/// </summary>
public sealed record UiFrame
{
	/// <summary>A fixed width.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	[JsonPropertyOrder(0)]
	public UiLength? Width
	{
		get;
		init => field = UiModifierValidation.NonNegative(value, nameof(Width));
	}

	/// <summary>A fixed height.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	[JsonPropertyOrder(1)]
	public UiLength? Height
	{
		get;
		init => field = UiModifierValidation.NonNegative(value, nameof(Height));
	}

	/// <summary>The narrowest the wrapper may be.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	[JsonPropertyOrder(2)]
	public UiLength? MinWidth
	{
		get;
		init => field = UiModifierValidation.NonNegative(value, nameof(MinWidth));
	}

	/// <summary>The widest the wrapper may be.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	[JsonPropertyOrder(3)]
	public UiLength? MaxWidth
	{
		get;
		init => field = UiModifierValidation.NonNegative(value, nameof(MaxWidth));
	}

	/// <summary>The shortest the wrapper may be.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	[JsonPropertyOrder(4)]
	public UiLength? MinHeight
	{
		get;
		init => field = UiModifierValidation.NonNegative(value, nameof(MinHeight));
	}

	/// <summary>The tallest the wrapper may be.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The length is negative.</exception>
	[JsonPropertyOrder(5)]
	public UiLength? MaxHeight
	{
		get;
		init => field = UiModifierValidation.NonNegative(value, nameof(MaxHeight));
	}

	/// <summary>Width over height, greater than <c>0</c>.</summary>
	/// <exception cref="ArgumentOutOfRangeException">The ratio is not a finite number above zero.</exception>
	[JsonPropertyOrder(6)]
	public double? AspectRatio
	{
		get;
		init => field = value is null || (double.IsFinite(value.Value) && value.Value > 0)
			? value
			: throw new ArgumentOutOfRangeException(nameof(AspectRatio), value, "An aspect ratio must be above zero.");
	}
}

internal static class UiModifierValidation
{
	internal static double Fraction(double value, string name)
		=> value is >= 0 and <= 1
			? value
			: throw new ArgumentOutOfRangeException(name, value, "The value must lie in 0..1.");

	internal static double Finite(double value, string name)
		=> double.IsFinite(value)
			? value
			: throw new ArgumentOutOfRangeException(name, value, "The value must be finite.");

	internal static UiLength? NonNegative(UiLength? length, string name)
		=> length is null ||
			(length.Basis >= 0 && length.MaxOfCross is null or >= 0 && length.MaxOfCell is null or >= 0)
				? length
				: throw new ArgumentOutOfRangeException(name, length, "A length must not be negative.");

	internal static IReadOnlyList<TStop> Stops<TStop>(IReadOnlyList<TStop> stops)
	{
		ArgumentNullException.ThrowIfNull(stops);

		return stops.Count >= 2
			? [.. stops]
			: throw new ArgumentException("A gradient needs at least two stops.", nameof(stops));
	}

	internal static string HexColor(string value, string name)
		=> value is not null && System.Text.RegularExpressions.Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$")
			? value
			: throw new ArgumentException("A colour must be written #rrggbb.", name);
}

internal sealed record UiShapeWire<TStop>
{
	[JsonPropertyOrder(0)]
	public UiLinearWire<TStop>? Linear { get; init; }

	[JsonPropertyOrder(1)]
	public UiRadialWire<TStop>? Radial { get; init; }
}

internal sealed record UiLinearWire<TStop>
{
	[JsonPropertyOrder(0)]
	public double Angle { get; init; }

	[JsonPropertyOrder(1)]
	public IReadOnlyList<TStop> Stops { get; init; } = [];
}

internal sealed record UiRadialWire<TStop>
{
	[JsonPropertyOrder(0)]
	public double CenterX { get; init; }

	[JsonPropertyOrder(1)]
	public double CenterY { get; init; }

	[JsonPropertyOrder(2)]
	public IReadOnlyList<TStop> Stops { get; init; } = [];
}

internal sealed class UiGradientJsonConverter : JsonConverter<UiGradient>
{
	public override UiGradient Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> JsonSerializer.Deserialize<UiShapeWire<UiGradientStop>>(ref reader, options) switch
		{
			{ Linear: { } linear } => UiGradient.Linear(linear.Angle, linear.Stops),
			{ Radial: { } radial } => UiGradient.Radial(radial.CenterX, radial.CenterY, radial.Stops),
			_ => throw new JsonException("A gradient is either linear or radial."),
		};

	public override void Write(Utf8JsonWriter writer, UiGradient value, JsonSerializerOptions options)
		=> JsonSerializer.Serialize(writer,
			value.IsRadial
				? new UiShapeWire<UiGradientStop>
				{
					Radial = new UiRadialWire<UiGradientStop>
					{
						CenterX = value.CenterX, CenterY = value.CenterY, Stops = value.Stops,
					},
				}
				: new UiShapeWire<UiGradientStop>
				{
					Linear = new UiLinearWire<UiGradientStop> { Angle = value.Angle, Stops = value.Stops },
				},
			options);
}

internal sealed class UiMaskJsonConverter : JsonConverter<UiMask>
{
	public override UiMask Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> JsonSerializer.Deserialize<UiShapeWire<UiMaskStop>>(ref reader, options) switch
		{
			{ Linear: { } linear } => UiMask.Linear(linear.Angle, linear.Stops),
			{ Radial: { } radial } => UiMask.Radial(radial.CenterX, radial.CenterY, radial.Stops),
			_ => throw new JsonException("A mask is either linear or radial."),
		};

	public override void Write(Utf8JsonWriter writer, UiMask value, JsonSerializerOptions options)
		=> JsonSerializer.Serialize(writer,
			value.IsRadial
				? new UiShapeWire<UiMaskStop>
				{
					Radial = new UiRadialWire<UiMaskStop>
					{
						CenterX = value.CenterX, CenterY = value.CenterY, Stops = value.Stops,
					},
				}
				: new UiShapeWire<UiMaskStop>
				{
					Linear = new UiLinearWire<UiMaskStop> { Angle = value.Angle, Stops = value.Stops },
				},
			options);
}

internal sealed class UiBackgroundJsonConverter : JsonConverter<UiBackground>
{
	public override UiBackground Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
		=> reader.TokenType == JsonTokenType.String
			? UiBackground.Solid(reader.GetString()!)
			: UiBackground.Of(JsonSerializer.Deserialize<UiGradient>(ref reader, options) ??
				throw new JsonException("A background is a colour or a gradient."));

	public override void Write(Utf8JsonWriter writer, UiBackground value, JsonSerializerOptions options)
	{
		if (value.Gradient is { } gradient)
		{
			JsonSerializer.Serialize(writer, gradient, options);

			return;
		}

		writer.WriteStringValue(value.Color);
	}
}
