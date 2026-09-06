using System.Globalization;

namespace MacroDeckHost.Integrations.Mouse.Actions;

internal static class MouseActionValues
{
	public static string ReadString(IReadOnlyDictionary<string, object> parameters, string name)
	{
		parameters.TryGetValue(name, out var value);
		return value?.ToString() ?? string.Empty;
	}

	public static int ReadInt(IReadOnlyDictionary<string, object> parameters, string name, int fallback)
	{
		if (!parameters.TryGetValue(name, out var value))
		{
			return fallback;
		}

		return value switch
		{
			int i => i,
			long l => (int)l,
			double d => (int)Math.Round(d),
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedDouble)
				=> (int)Math.Round(parsedDouble),
			_ => fallback
		};
	}

	public static MouseButton ReadButton(IReadOnlyDictionary<string, object> parameters, string name = "button")
		=> ReadString(parameters, name).Trim().ToLowerInvariant() switch
		{
			"right" => MouseButton.Right,
			"middle" => MouseButton.Middle,
			"back" => MouseButton.Back,
			"forward" => MouseButton.Forward,
			_ => MouseButton.Left
		};

	public static MouseTarget ReadTarget(
		IReadOnlyDictionary<string, object> parameters,
		string modeName = "positionMode",
		string xName = "x",
		string yName = "y",
		MouseCoordinateMode fallbackMode = MouseCoordinateMode.Current)
	{
		var mode = ReadString(parameters, modeName).Trim().ToLowerInvariant() switch
		{
			"absolute" => MouseCoordinateMode.Absolute,
			"relative" => MouseCoordinateMode.Relative,
			"current" => MouseCoordinateMode.Current,
			_ => fallbackMode
		};

		if (mode == MouseCoordinateMode.Current)
		{
			return MouseTarget.Current;
		}

		return new MouseTarget(mode, ReadInt(parameters, xName, 0), ReadInt(parameters, yName, 0));
	}

	public static (ScrollAxis Axis, int Sign) ReadScrollDirection(
		IReadOnlyDictionary<string, object> parameters,
		string name = "direction")
		=> ReadString(parameters, name).Trim().ToLowerInvariant() switch
		{
			"up" => (ScrollAxis.Vertical, 1),
			"left" => (ScrollAxis.Horizontal, -1),
			"right" => (ScrollAxis.Horizontal, 1),
			_ => (ScrollAxis.Vertical, -1)
		};
}
