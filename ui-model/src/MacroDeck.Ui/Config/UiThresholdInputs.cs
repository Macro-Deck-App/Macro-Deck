using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Config;

/// <summary>
/// One coloured range of a <see cref="UiThresholds" /> value: it starts at <see cref="From" /> and ends
/// where the next band starts.
/// </summary>
/// <param name="Id">A stable key, unique within the value. Editors keep it across moves and recolours, so
/// a band can be recognised after the user edits it.</param>
/// <param name="Color">The band's colour as <c>#rgb</c> or <c>#rrggbb</c>, normalised to lowercase
/// <c>#rrggbb</c>.</param>
/// <param name="From">Where the band starts. Absent on the first band, which covers everything below the
/// second band's start; required and strictly increasing on every later band.</param>
public sealed record UiThresholdBand(string Id, string Color, double? From = null);

/// <summary>
/// A numeric range divided into coloured bands: the value of a <see cref="UiThresholdsInput" />, and the
/// shape Macro Deck stores colour thresholds in.
///
/// <para>
/// The constructor validates: at least one and at most <see cref="MaxBandCount" /> bands, non-empty
/// unique ids, a valid colour on every band, no start on the first band and finite, strictly increasing
/// starts on the others. An invalid list throws <see cref="ArgumentException" />. Read stored or received
/// JSON with <see cref="TryParse" />, which never throws.
/// </para>
/// </summary>
[JsonConverter(typeof(UiThresholdsJsonConverter))]
public sealed partial record UiThresholds
{
	/// <summary>The most bands a value may carry.</summary>
	public const int MaxBandCount = 64;

	/// <summary>Creates a value from its bands, normalising each colour.</summary>
	/// <exception cref="ArgumentException">The bands break one of the rules in the type's remarks.</exception>
	public UiThresholds(IReadOnlyList<UiThresholdBand> bands)
	{
		ArgumentNullException.ThrowIfNull(bands);

		if (Normalize(bands, out var normalized) is { } error)
		{
			throw new ArgumentException(error, nameof(bands));
		}

		Bands = normalized;
	}

	/// <summary>The bands, lowest first.</summary>
	public IReadOnlyList<UiThresholdBand> Bands { get; }

	/// <summary>The band <paramref name="value" /> falls in: the last band whose start is at or below it,
	/// and the first band for anything below the second band's start. <c>null</c> for NaN.</summary>
	public UiThresholdBand? BandAt(double value)
	{
		if (double.IsNaN(value))
		{
			return null;
		}

		var band = Bands[0];

		for (var i = 1; i < Bands.Count; i++)
		{
			if (value >= Bands[i].From)
			{
				band = Bands[i];
			}
		}

		return band;
	}

	/// <summary>The colour of the band <paramref name="value" /> falls in, or <c>null</c> for NaN. See
	/// <see cref="BandAt" />.</summary>
	public string? ColorAt(double value) => BandAt(value)?.Color;

	/// <summary>Reads a value from JSON in the shape a <see cref="UiThresholdsInput" /> sends and Macro
	/// Deck stores. Returns <c>false</c> for anything that is not a valid value, including JSON null.
	/// </summary>
	public static bool TryParse(JsonElement element, out UiThresholds? thresholds)
	{
		thresholds = null;

		if (element.ValueKind is not JsonValueKind.Object)
		{
			return false;
		}

		try
		{
			thresholds = element.Deserialize<UiThresholds>(UiCanonicalJson.Options);
		}
		catch (JsonException)
		{
			return false;
		}

		return thresholds is not null;
	}

	/// <inheritdoc />
	public bool Equals(UiThresholds? other) => other is not null && Bands.SequenceEqual(other.Bands);

	/// <inheritdoc />
	public override int GetHashCode()
	{
		var hash = new HashCode();

		foreach (var band in Bands)
		{
			hash.Add(band);
		}

		return hash.ToHashCode();
	}

	internal static string? Normalize(IReadOnlyList<UiThresholdBand> bands, out IReadOnlyList<UiThresholdBand> normalized)
	{
		normalized = [];

		if (bands.Count is 0 or > MaxBandCount)
		{
			return $"A thresholds value carries 1 to {MaxBandCount} bands, not {bands.Count}.";
		}

		var ids = new HashSet<string>(StringComparer.Ordinal);
		var result = new UiThresholdBand[bands.Count];

		for (var i = 0; i < bands.Count; i++)
		{
			var band = bands[i];

			if (band is null || string.IsNullOrEmpty(band.Id) || !ids.Add(band.Id))
			{
				return $"Band {i} needs a non-empty id that no other band uses.";
			}

			if (NormalizeColor(band.Color) is not { } color)
			{
				return $"Band '{band.Id}' has no valid #rgb or #rrggbb colour.";
			}

			if (i == 0 && band.From is not null)
			{
				return "The first band starts with the range, so it carries no 'from'.";
			}

			if (i > 0 &&
				(band.From is not { } from ||
					!double.IsFinite(from) ||
					(i > 1 && from <= bands[i - 1].From)))
			{
				return $"Band '{band.Id}' needs a finite 'from' above the previous band's.";
			}

			result[i] = band with { Color = color };
		}

		normalized = result;

		return null;
	}

	private static string? NormalizeColor(string? color)
	{
		if (color is null || !ColorPattern().IsMatch(color))
		{
			return null;
		}

		var hex = color[1..].ToLowerInvariant();

		return hex.Length == 3
			? string.Create(CultureInfo.InvariantCulture, $"#{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}")
			: "#" + hex;
	}

	[GeneratedRegex("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6})$")]
	private static partial Regex ColorPattern();
}

internal sealed class UiThresholdsJsonConverter : JsonConverter<UiThresholds>
{
	private const string _bands = "bands";
	private const string _id = "id";
	private const string _color = "color";
	private const string _from = "from";

	public override UiThresholds Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType is not JsonTokenType.StartObject)
		{
			throw new JsonException("A thresholds value is a JSON object.");
		}

		List<UiThresholdBand>? bands = null;

		while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
		{
			var name = reader.GetString();
			reader.Read();

			if (name == _bands)
			{
				bands = ReadBands(ref reader);
			}
			else
			{
				reader.Skip();
			}
		}

		if (bands is null)
		{
			throw new JsonException("A thresholds value carries a 'bands' array.");
		}

		if (UiThresholds.Normalize(bands, out _) is { } error)
		{
			throw new JsonException(error);
		}

		return new UiThresholds(bands);
	}

	public override void Write(Utf8JsonWriter writer, UiThresholds value, JsonSerializerOptions options)
	{
		writer.WriteStartObject();
		writer.WriteStartArray(_bands);

		foreach (var band in value.Bands)
		{
			writer.WriteStartObject();
			writer.WriteString(_id, band.Id);
			writer.WriteString(_color, band.Color);

			if (band.From is { } from)
			{
				writer.WriteNumber(_from, from);
			}

			writer.WriteEndObject();
		}

		writer.WriteEndArray();
		writer.WriteEndObject();
	}

	private static List<UiThresholdBand> ReadBands(ref Utf8JsonReader reader)
	{
		if (reader.TokenType is not JsonTokenType.StartArray)
		{
			throw new JsonException("'bands' is a JSON array.");
		}

		var bands = new List<UiThresholdBand>();

		while (reader.Read() && reader.TokenType is not JsonTokenType.EndArray)
		{
			if (bands.Count == UiThresholds.MaxBandCount)
			{
				throw new JsonException($"A thresholds value carries at most {UiThresholds.MaxBandCount} bands.");
			}

			bands.Add(ReadBand(ref reader));
		}

		return bands;
	}

	private static UiThresholdBand ReadBand(ref Utf8JsonReader reader)
	{
		if (reader.TokenType is not JsonTokenType.StartObject)
		{
			throw new JsonException("Each band is a JSON object.");
		}

		string? id = null;
		string? color = null;
		double? from = null;

		while (reader.Read() && reader.TokenType is not JsonTokenType.EndObject)
		{
			var name = reader.GetString();
			reader.Read();

			switch (name)
			{
				case _id:
					id = ReadString(ref reader, name);

					break;
				case _color:
					color = ReadString(ref reader, name);

					break;
				case _from when reader.TokenType is JsonTokenType.Null:
					break;
				case _from when reader.TokenType is JsonTokenType.Number:
					from = reader.GetDouble();

					break;
				case _from:
					throw new JsonException("A band's 'from' is a number.");
				default:
					reader.Skip();

					break;
			}
		}

		return new UiThresholdBand(id ?? string.Empty, color ?? string.Empty, from);
	}

	private static string ReadString(ref Utf8JsonReader reader, string? name)
		=> reader.TokenType is JsonTokenType.String
			? reader.GetString()!
			: throw new JsonException($"A band's '{name}' is a string.");
}

/// <summary>
/// A colour-threshold editor: a bar over <see cref="Min" />..<see cref="Max" /> divided into coloured
/// bands, with a draggable handle between each pair, the boundary values shown in <see cref="Unit" />, and
/// a list to recolour, add and remove bands. The value is a <see cref="UiThresholds" />.
///
/// <para>
/// <b>Null means "use the defaults".</b> Bind a null until the user edits, give the defaults as
/// <see cref="UiInput{T}.DefaultValue" />, and the editor shows them; with
/// <see cref="UiInput{T}.SupportsReset" /> its reset writes null again rather than a copy of the defaults,
/// so a value that depends on another setting keeps following it.
/// </para>
///
/// <para>
/// <b>What the user may change.</b> <see cref="UiInput{T}.Disabled" /> makes the editor read-only.
/// <see cref="FixedCount" /> keeps the number of bands, <see cref="FixedColors" /> keeps every band's
/// colour and allows no new band, and <see cref="MaxCount" /> caps adding. A change that breaks one is
/// rejected before it reaches a writable binding; a read-only binding paired with its own
/// <c>change</c> handler receives every change unchecked. A widget's stored configuration is drafted by
/// the client and can hold any valid value, so read it with <see cref="UiThresholds.TryParse" />.
/// </para>
/// </summary>
public sealed record UiThresholdsInput : UiInput<UiThresholds>
{
	/// <summary>The start of the bar.</summary>
	public UiValue<double> Min { get; init; }

	/// <summary>The end of the bar. Boundaries outside <see cref="Min" />..<see cref="Max" /> widen the bar
	/// instead of being moved.</summary>
	public UiValue<double> Max { get; init; }

	/// <summary>The increment a handle moves by, and the narrowest a band can be dragged.</summary>
	public UiValue<double> Step { get; init; }

	/// <summary>The unit drawn after every value, such as <c>%</c> or <c>°C</c>.</summary>
	public UiText Unit { get; init; }

	/// <summary>Whether the user may not add or remove bands. The count is the default's.</summary>
	public UiValue<bool> FixedCount { get; init; }

	/// <summary>Whether the user may not change a band's colour or add a band.</summary>
	public UiValue<bool> FixedColors { get; init; }

	/// <summary>The most bands the user may add up to.</summary>
	public UiValue<int> MaxCount { get; init; }

	/// <inheritdoc />
	public override string Type => UiConfigPrimitives.Thresholds;

	/// <inheritdoc />
	protected internal override void DeclareProperties(UiPropertyDeclaration properties)
	{
		ArgumentNullException.ThrowIfNull(properties);

		base.DeclareProperties(properties);

		properties.Set(UiConfigProperties.Min, Min);
		properties.Set(UiConfigProperties.Max, Max);
		properties.Set(UiConfigProperties.Step, Step);
		properties.Set(UiConfigProperties.Unit, Unit.Value);
		properties.Set(UiConfigProperties.FixedCount, FixedCount);
		properties.Set(UiConfigProperties.FixedColors, FixedColors);
		properties.Set(UiConfigProperties.MaxCount, MaxCount);
	}

	private protected override bool AcceptsValue(UiThresholds? value, out string? rejection)
	{
		rejection = null;

		if (Disabled.TryEvaluate(out var disabled) && disabled)
		{
			rejection = "The thresholds editor is disabled.";

			return false;
		}

		if (value is null)
		{
			return true;
		}

		var current = Binding.Value.TryEvaluate(out var bound) ? bound : null;
		var defaults = DefaultValue.TryEvaluate(out var declared) ? declared : null;
		var count = value.Bands.Count;

		if (MaxCount.TryEvaluate(out var maxCount) &&
			count > maxCount &&
			count > ((current ?? defaults)?.Bands.Count ?? 0))
		{
			rejection = $"At most {maxCount} bands may be added.";

			return false;
		}

		if (FixedCount.TryEvaluate(out var fixedCount) &&
			fixedCount &&
			(defaults ?? current) is { } counted &&
			count != counted.Bands.Count)
		{
			rejection = $"The number of bands is fixed at {counted.Bands.Count}.";

			return false;
		}

		if (FixedColors.TryEvaluate(out var fixedColors) && fixedColors && (current ?? defaults) is { } reference)
		{
			foreach (var band in value.Bands)
			{
				if (reference.Bands.FirstOrDefault(candidate => candidate.Id == band.Id) is not { } kept ||
					kept.Color != band.Color)
				{
					rejection = $"Band colours are fixed, so band '{band.Id}' cannot be added or recoloured.";

					return false;
				}
			}
		}

		return true;
	}
}
