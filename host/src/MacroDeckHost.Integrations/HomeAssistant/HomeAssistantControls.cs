using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal enum HomeAssistantControlSource
{
	Attribute,
	State,
	BrightnessPercent
}

internal enum HomeAssistantControlNumber
{
	Decimal,
	Rounded,
	Floored
}

internal sealed record HomeAssistantServiceRequest(string Service, IReadOnlyDictionary<string, object?>? Data);

internal sealed record HomeAssistantControl(string Domain, string Leaf, string Service, string Field)
{
	private const string BrightnessAttribute = "brightness";
	private const double MaxBrightness = 255;

	public HomeAssistantControlSource Source { get; init; } = HomeAssistantControlSource.Attribute;

	public HomeAssistantControlNumber Number { get; init; } = HomeAssistantControlNumber.Decimal;

	public double? Min { get; init; }

	public double? Max { get; init; }

	public double? Step { get; init; }

	public string? MinAttribute { get; init; }

	public string? MaxAttribute { get; init; }

	public string? StepAttribute { get; init; }

	public string? Unit { get; init; }

	public string? SemanticKind { get; init; }

	public int? DecimalPlaces { get; init; }

	public string? ZeroService { get; init; }

	public bool ZeroWhenOff { get; init; }

	public double? Value(HomeAssistantEntityState state)
		=> Source switch
		{
			HomeAssistantControlSource.State => Parse(state.State),
			HomeAssistantControlSource.BrightnessPercent => BrightnessPercent(state),
			_ => Attribute(state, Leaf) ?? (ZeroWhenOff && IsOff(state) ? 0 : null)
		};

	public bool IsOffered(HomeAssistantEntityState state)
		=> Source switch
		{
			HomeAssistantControlSource.State => true,
			HomeAssistantControlSource.BrightnessPercent => HasAttribute(state, BrightnessAttribute) || IsDimmable(state),
			_ => HasAttribute(state, Leaf)
		};

	public VariableReading Reading(HomeAssistantEntityState state, object? value)
		=> VariableReading.Of(Source == HomeAssistantControlSource.BrightnessPercent ? Value(state) : value ?? Value(state),
			Attribute(state, MinAttribute) ?? Min,
			Attribute(state, MaxAttribute) ?? Max,
			Attribute(state, StepAttribute) is { } step and > 0 ? step : Step);

	public HomeAssistantServiceRequest Request(HomeAssistantEntityState state, double value)
	{
		var min = Attribute(state, MinAttribute) ?? Min;
		var max = Attribute(state, MaxAttribute) ?? Max;
		var clamped = min is { } low && max is { } high && high > low ? Math.Clamp(value, low, high) : value;

		if (ZeroService is not null && clamped <= 0)
		{
			return new HomeAssistantServiceRequest(ZeroService, null);
		}

		object encoded = Number switch
		{
			HomeAssistantControlNumber.Rounded => (long)Math.Round(clamped, MidpointRounding.AwayFromZero),
			// Home Assistant truncates a percentage to an integer: 66.67 on a three speed fan has to
			// stay speed two, and rounding it up to 67 would select speed three.
			HomeAssistantControlNumber.Floored => (long)Math.Floor(clamped + 1e-9),
			_ => clamped
		};

		return new HomeAssistantServiceRequest(Service,
			new Dictionary<string, object?>(StringComparer.Ordinal) { [Field] = encoded });
	}

	private static double? BrightnessPercent(HomeAssistantEntityState state)
	{
		if (Attribute(state, BrightnessAttribute) is { } brightness)
		{
			var percent = Math.Round(brightness / MaxBrightness * 100, MidpointRounding.AwayFromZero);
			return brightness > 0 ? Math.Max(1, percent) : 0;
		}

		return IsOff(state) ? 0 : null;
	}

	private static bool IsOff(HomeAssistantEntityState state)
		=> string.Equals(state.State, "off", StringComparison.Ordinal);

	private static bool IsDimmable(HomeAssistantEntityState state)
		=> state.ReadStringList("supported_color_modes")
			.Any(mode => mode is not ("onoff" or "unknown"));

	private static bool HasAttribute(HomeAssistantEntityState state, string name)
		=> state.Attributes.ValueKind == JsonValueKind.Object && state.Attributes.TryGetProperty(name, out _);

	private static double? Attribute(HomeAssistantEntityState state, string? name)
		=> name is not null &&
			state.Attributes.ValueKind == JsonValueKind.Object &&
			state.Attributes.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetDouble(out var number) &&
			double.IsFinite(number)
				? number
				: null;

	private static double? Parse(string text)
		=> double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
			double.IsFinite(number)
				? number
				: null;
}

internal static class HomeAssistantControls
{
	private const string Percent = "%";

	private static readonly HomeAssistantControl[] _all =
	[
		new("light", "brightness_pct", "turn_on", "brightness_pct")
		{
			Source = HomeAssistantControlSource.BrightnessPercent,
			Number = HomeAssistantControlNumber.Rounded,
			Min = 0,
			Max = 100,
			Step = 1,
			Unit = Percent,
			SemanticKind = VariableSemanticKinds.Percentage,
			DecimalPlaces = 0,
			ZeroService = "turn_off"
		},
		new("light", "color_temp_kelvin", "turn_on", "color_temp_kelvin")
		{
			Number = HomeAssistantControlNumber.Rounded,
			MinAttribute = "min_color_temp_kelvin",
			MaxAttribute = "max_color_temp_kelvin",
			Step = 1,
			Unit = "K"
		},
		new("fan", "percentage", "set_percentage", "percentage")
		{
			Number = HomeAssistantControlNumber.Floored,
			Min = 0,
			Max = 100,
			Step = 1,
			StepAttribute = "percentage_step",
			ZeroWhenOff = true,
			Unit = Percent,
			SemanticKind = VariableSemanticKinds.Percentage,
			DecimalPlaces = 0
		},
		Position("cover", "current_position", "set_cover_position", "position"),
		Position("cover", "current_tilt_position", "set_cover_tilt_position", "tilt_position"),
		Position("valve", "current_position", "set_valve_position", "position"),
		new("media_player", "volume_level", "volume_set", "volume_level")
		{
			Min = 0,
			Max = 1,
			Step = 0.01,
			DecimalPlaces = 2
		},
		Temperature("climate"),
		Temperature("water_heater"),
		Humidity("climate"),
		Humidity("humidifier"),
		Number("number"),
		Number("input_number")
	];

	public static HomeAssistantControl? Find(string entityId, string leaf)
	{
		var domain = HomeAssistantEntityState.DomainOf(entityId);

		return Array.Find(_all,
			control => string.Equals(control.Domain, domain, StringComparison.Ordinal) &&
				string.Equals(control.Leaf, leaf, StringComparison.Ordinal));
	}

	public static IEnumerable<HomeAssistantControl> For(string domain)
		=> _all.Where(control => string.Equals(control.Domain, domain, StringComparison.Ordinal));

	public static bool TryReadNumber(object? value, out double number)
	{
		number = value switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => double.NaN
		};

		return double.IsFinite(number);
	}

	private static HomeAssistantControl Position(string domain, string leaf, string service, string field)
		=> new(domain, leaf, service, field)
		{
			Number = HomeAssistantControlNumber.Rounded,
			Min = 0,
			Max = 100,
			Step = 1,
			Unit = Percent,
			SemanticKind = VariableSemanticKinds.Percentage,
			DecimalPlaces = 0
		};

	private static HomeAssistantControl Temperature(string domain)
		=> new(domain, "temperature", "set_temperature", "temperature")
		{
			MinAttribute = "min_temp",
			MaxAttribute = "max_temp",
			StepAttribute = "target_temp_step"
		};

	private static HomeAssistantControl Humidity(string domain)
		=> new(domain, "humidity", "set_humidity", "humidity")
		{
			Number = HomeAssistantControlNumber.Rounded,
			Min = 0,
			Max = 100,
			Step = 1,
			MinAttribute = "min_humidity",
			MaxAttribute = "max_humidity",
			Unit = Percent,
			SemanticKind = VariableSemanticKinds.Percentage,
			DecimalPlaces = 0
		};

	private static HomeAssistantControl Number(string domain)
		=> new(domain, "state", "set_value", "value")
		{
			Source = HomeAssistantControlSource.State,
			MinAttribute = "min",
			MaxAttribute = "max",
			StepAttribute = "step"
		};
}
