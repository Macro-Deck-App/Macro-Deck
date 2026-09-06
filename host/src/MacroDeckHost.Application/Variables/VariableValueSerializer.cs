using System.Globalization;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

public static class VariableValueSerializer
{
	public static string Serialize(VariableType type, object? raw, int? decimalPlaces)
	{
		switch (type)
		{
			case VariableType.Text:
				return raw?.ToString() ?? string.Empty;

			case VariableType.Numeric:
			{
				if (raw is null)
				{
					return "0";
				}

				var dec = raw switch
				{
					decimal d => d,
					double db => (decimal)db,
					float f => (decimal)f,
					long l => l,
					int i => i,
					string s => decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
						? parsed
						: 0m,
					_ => Convert.ToDecimal(raw, CultureInfo.InvariantCulture)
				};
				return decimalPlaces.HasValue
					? dec.ToString("F" + decimalPlaces.Value, CultureInfo.InvariantCulture)
					: dec.ToString(CultureInfo.InvariantCulture);
			}

			case VariableType.Boolean:
			{
				var b = raw switch
				{
					bool actual => actual,
					string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s == "1",
					int i => i != 0,
					_ => raw is not null
				};
				return b ? "true" : "false";
			}

			default:
				throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported variable type");
		}
	}

	public static object? Deserialize(VariableType type, string value)
	{
		return type switch
		{
			VariableType.Text => value,
			VariableType.Numeric => decimal.TryParse(value,
				NumberStyles.Number,
				CultureInfo.InvariantCulture,
				out var d)
				? d
				: 0m,
			VariableType.Boolean => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase),
			_ => null
		};
	}
}
