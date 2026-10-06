using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal sealed class SetInputSettingActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string InputParameter = "input";
	internal const string SettingParameter = "setting";
	internal const string ValueParameter = "value";

	private const double LongRangeEnd = 9223372036854775808d;

	private readonly ObsTargetResolver _resolver;

	public SetInputSettingActionDefinition(ObsTargetResolver resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-input-setting";

	public LocalizedText Name => AppStrings.Integrations.Obs.Actions.SetInputSetting.Name();

	public LocalizedText Description => AppStrings.Integrations.Obs.Actions.SetInputSetting.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ObsTargetResolver.Parameter(),
		ActionParameter.DynamicChoice(InputParameter,
			label: AppStrings.Integrations.Obs.Params.Input(),
			required: true),
		ActionParameter.Autocomplete(SettingParameter,
			label: AppStrings.Integrations.Obs.Actions.SetInputSetting.SettingLabel(),
			description: AppStrings.Integrations.Obs.Actions.SetInputSetting.SettingDescription(),
			required: true),
		ActionParameter.MultilineText(ValueParameter,
			label: AppStrings.Integrations.Obs.Actions.SetInputSetting.ValueLabel(),
			description: AppStrings.Integrations.Obs.Actions.SetInputSetting.ValueDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == ObsTargetResolver.ConfigurationParameter)
		{
			return _resolver.ConfigurationOptions();
		}

		var connection = _resolver.ForOptions(context.CurrentParameters);
		IReadOnlyList<string> values = [];

		if (connection is not null)
		{
			if (context.ParameterName == InputParameter)
			{
				values = await connection.GetInputNamesAsync();
			}
			else if (context.ParameterName == SettingParameter &&
				context.CurrentParameters.GetValueOrDefault(InputParameter) is string input &&
				!string.IsNullOrWhiteSpace(input))
			{
				values = await SettingKeysAsync(connection, input, context.Filter);
			}
		}

		return new DynamicOptionsResult
		{
			Options = values.Select(v => new ActionParameterOption { Value = v, Label = v }).ToList(),
			CacheSeconds = 5
		};
	}

	private static async Task<IReadOnlyList<string>> SettingKeysAsync(
		ObsConnection connection,
		string input,
		string? filter)
	{
		var keys = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var json in new[]
			{
				await connection.GetInputSettingsJsonAsync(input),
				await connection.GetInputDefaultSettingsJsonAsync(input)
			})
		{
			if (json is not null && ObsSettingsJson.TryGetKeys(json, out var found))
			{
				keys.UnionWith(found);
			}
		}

		return string.IsNullOrWhiteSpace(filter)
			? keys.ToList()
			: keys.Where(key => key.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetInputSettingActionDefinition>(ObsIntegration.IntegrationId);

		private readonly ObsTargetResolver _resolver;

		public Executor(ObsTargetResolver resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var connection, out var error))
			{
				return error;
			}

			if (context.Parameters.GetValueOrDefault(InputParameter) is not string input ||
				string.IsNullOrWhiteSpace(input))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoInputSelected());
			}

			var setting = (context.Parameters.GetValueOrDefault(SettingParameter) as string)?.Trim();
			if (string.IsNullOrEmpty(setting))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Obs.Errors.NoSettingSelected());
			}

			var current = await connection.GetInputSettingsJsonAsync(input);
			if (current is null || !ObsSettingsJson.TryGetValueKind(current, setting, out var kind))
			{
				return ReadFailed(connection, input);
			}

			if (kind is null)
			{
				var defaults = await connection.GetInputDefaultSettingsJsonAsync(input);
				if (defaults is null || !ObsSettingsJson.TryGetValueKind(defaults, setting, out kind))
				{
					return ReadFailed(connection, input);
				}
			}

			var text = ValueText(context.Parameters.GetValueOrDefault(ValueParameter));
			if (!TryConvert(kind, text, out var value))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter, kind switch
				{
					JsonValueKind.True or JsonValueKind.False =>
						AppStrings.Integrations.Obs.Errors.SettingExpectsBoolean(setting: setting),
					JsonValueKind.Number => AppStrings.Integrations.Obs.Errors.SettingExpectsNumber(setting: setting),
					_ => AppStrings.Integrations.Obs.Errors.SettingExpectsJson(setting: setting)
				});
			}

			var settings = new JsonObject { [setting] = value }.ToJsonString();
			return ObsCommandResults.ToActionResult(await connection.SetInputSettingsAsync(input, settings));
		}

		private static ActionResult ReadFailed(ObsConnection connection, string input)
		{
			if (!connection.IsConnected)
			{
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Obs.Errors.NotConnected());
			}

			_logger.Warning("OBS set-input-setting action skipped: settings unavailable for '{Input}'", input);
			return ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.Obs.Errors.InputNotFound(input: input));
		}

		private static string ValueText(object? raw) => raw switch
		{
			null => string.Empty,
			string text => text,
			bool flag => flag ? "true" : "false",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => raw.ToString() ?? string.Empty
		};

		private static bool TryConvert(JsonValueKind? kind, string text, out JsonNode? value)
		{
			value = null;

			switch (kind)
			{
				case JsonValueKind.True or JsonValueKind.False:
					if (!bool.TryParse(text.Trim(), out var flag))
					{
						return false;
					}

					value = JsonValue.Create(flag);
					return true;

				case JsonValueKind.Number:
					return TryConvertNumber(text.Trim(), out value);

				case JsonValueKind.Object or JsonValueKind.Array:
					try
					{
						value = JsonNode.Parse(text);
					}
					catch (JsonException)
					{
						return false;
					}

					return value is not null && value.GetValueKind() == kind;

				default:
					value = JsonValue.Create(text);
					return true;
			}
		}

		private static bool TryConvertNumber(string text, out JsonNode? value)
		{
			value = null;

			if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var whole))
			{
				value = JsonValue.Create(whole);
				return true;
			}

			if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ||
				!double.IsFinite(number))
			{
				return false;
			}

			value = Math.Floor(number) == number && number >= -LongRangeEnd && number < LongRangeEnd
				? JsonValue.Create((long)number)
				: JsonValue.Create(number);
			return true;
		}
	}
}
