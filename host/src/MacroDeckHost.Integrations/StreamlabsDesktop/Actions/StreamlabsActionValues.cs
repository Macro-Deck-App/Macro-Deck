using System.Globalization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal static class StreamlabsActionValues
{
	public const string SceneParameter = "scene";

	public const string SourceParameter = "source";

	public const string ModeParameter = "mode";

	public const string VariableParameter = "variable";

	public static string? ReadText(ActionExecutionContext context, string name)
	{
		var value = context.Parameters.GetValueOrDefault(name) as string;
		return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
	}

	public static string ReadMode(ActionExecutionContext context, string fallback)
		=> context.Parameters.GetValueOrDefault(ModeParameter) as string is { Length: > 0 } mode
			? mode
			: fallback;

	public static double? ReadNumber(object? raw) => raw switch
	{
		null => null,
		double value => value,
		float value => value,
		int value => value,
		long value => value,
		decimal value => (double)value,
		string text when double.TryParse(text,
			NumberStyles.Float,
			CultureInfo.InvariantCulture,
			out var parsed) => parsed,
		_ => null
	};

	public static async Task<DynamicOptionsResult> SceneOrSourceOptionsAsync(
		StreamlabsDesktopConnection? connection,
		DynamicOptionsContext context)
	{
		IReadOnlyList<string> values = [];

		if (connection is not null)
		{
			if (context.ParameterName == SourceParameter &&
				context.CurrentParameters.GetValueOrDefault(SceneParameter) is string scene &&
				!string.IsNullOrWhiteSpace(scene))
			{
				values = await connection.GetSceneItemNamesAsync(scene).ConfigureAwait(false);
			}
			else if (context.ParameterName == SceneParameter)
			{
				values = await connection.GetSceneNamesAsync().ConfigureAwait(false);
			}
		}

		return Options(values);
	}

	public static DynamicOptionsResult Options(IReadOnlyList<string> values) => new()
	{
		Options = values.Select(value => new ActionParameterOption { Value = value, Label = value }).ToList(),
		CacheSeconds = 5
	};
}
