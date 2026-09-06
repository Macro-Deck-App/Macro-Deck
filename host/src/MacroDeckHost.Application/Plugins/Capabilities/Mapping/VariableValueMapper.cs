using System.Globalization;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class VariableValueMapper
{
	public static object? ToDomain(VariableValueDto? dto)
		=> dto?.Kind switch
		{
			"text" => dto.Text,
			"number" => dto.Number,
			"boolean" => dto.Boolean,
			_ => null
		};

	public static VariableReading ToDomain(VariableReadingDto? dto)
		=> dto is null
			? VariableReading.Unavailable
			: VariableReading.Of(ToDomain(dto.Value), dto.Min, dto.Max, dto.Step);

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
}
