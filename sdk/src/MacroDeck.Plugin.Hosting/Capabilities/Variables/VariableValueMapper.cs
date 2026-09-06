using System.Globalization;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Hosting.Capabilities.Variables;

/// <summary>
/// Converts between a variable's CLR value and the wire's tagged union, in both directions: a reading
/// leaves through <see cref="ToDto(VariableReading)" />, a value to write arrives through
/// <see cref="ToDomain" />. See <see cref="VariableValueDto" />'s remarks on the degrade-to-unavailable
/// rule for any CLR shape that is not text, number or boolean.
/// </summary>
internal static class VariableValueMapper
{
	public static VariableValueDto ToDto(object? value)
		=> value switch
		{
			null => VariableValueDto.Unavailable,
			string text => new VariableValueDto { Kind = "text", Text = text },
			bool boolean => new VariableValueDto { Kind = "boolean", Boolean = boolean },
			sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal
				=> new VariableValueDto
				{
					Kind = "number", Number = Convert.ToDouble(value, CultureInfo.InvariantCulture)
				},
			_ => VariableValueDto.Unavailable
		};

	public static VariableReadingDto ToDto(VariableReading reading)
		=> new()
		{
			Value = ToDto(reading.Value), Min = reading.Min, Max = reading.Max, Step = reading.Step
		};

	/// <summary>
	/// The CLR value a <c>set</c> carries, in the three shapes <see cref="ToDto(object?)" /> produces.
	/// An unavailable - or otherwise unreadable - value maps to <c>null</c>, which a provider is free to
	/// refuse with <see cref="VariableWriteStatus.InvalidValue" />.
	/// </summary>
	public static object? ToDomain(VariableValueDto? value)
	{
		if (value is null)
		{
			return null;
		}

		return value.Kind switch
		{
			"text" => value.Text,
			"number" => value.Number,
			"boolean" => value.Boolean,
			_ => null
		};
	}
}
