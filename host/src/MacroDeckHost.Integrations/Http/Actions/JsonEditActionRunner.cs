using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Http.Actions;

internal readonly record struct JsonEditInput(
	JsonNode? Document,
	IReadOnlyList<JsonPathSegment> Path,
	string VariableName);

internal static class JsonEditActionRunner
{
	internal const string JsonParameter = "json";
	internal const string PathParameter = "path";
	internal const string ValueParameter = "value";
	internal const string ValueTypeParameter = "valueType";
	internal const string VariableParameter = "variable";

	private static readonly ILogger _logger =
		IntegrationLog.For(HttpIntegration.IntegrationId, typeof(JsonEditActionRunner));

	public static ActionParameter JsonInput()
	{
		return ActionParameter.MultilineText(JsonParameter,
			AppStrings.Integrations.Http.Actions.JsonInputLabel(),
			AppStrings.Integrations.Http.Actions.JsonInputDescription(),
			required: true);
	}

	public static ActionParameter Path(string? defaultValue = null)
	{
		return ActionParameter.Text(PathParameter,
			AppStrings.Integrations.Http.Actions.GetJsonValuePathLabel(),
			AppStrings.Integrations.Http.Actions.JsonEditPathDescription(),
			defaultValue: defaultValue,
			required: true);
	}

	public static IEnumerable<ActionParameter> Value()
	{
		yield return ActionParameter.Choice(ValueTypeParameter,
			[
				new ActionParameterOption
				{
					Value = "text",
					Label = AppStrings.Integrations.Http.Actions.JsonValueTypeText()
				},
				new ActionParameterOption
				{
					Value = "number",
					Label = AppStrings.Integrations.Http.Actions.JsonValueTypeNumber()
				},
				new ActionParameterOption
				{
					Value = "boolean",
					Label = AppStrings.Integrations.Http.Actions.JsonValueTypeBoolean()
				},
				new ActionParameterOption
				{
					Value = "null",
					Label = AppStrings.Integrations.Http.Actions.JsonValueTypeNull()
				},
				new ActionParameterOption
				{
					Value = "json",
					Label = AppStrings.Integrations.Http.Actions.JsonValueTypeJson()
				}
			],
			AppStrings.Integrations.Http.Actions.JsonValueTypeLabel(),
			defaultValue: "text");
		yield return ActionParameter.MultilineText(ValueParameter,
				AppStrings.Integrations.Http.Actions.JsonValueLabel())
			.OnlyWhen(ValueTypeParameter, "text", "number", "boolean", "json");
	}

	public static ActionParameter Variable()
	{
		return ActionParameter.Autocomplete(VariableParameter,
			AppStrings.Integrations.Http.Params.SaveToVariableLabel(),
			optionsSourceId: HostOptionsSources.UserVariables,
			required: true);
	}

	public static ActionResult? TryReadInput(
		IReadOnlyDictionary<string, object> parameters,
		bool blankPathIsRoot,
		out JsonEditInput input)
	{
		input = default;

		var variableName = HttpActionValues.ReadText(parameters, VariableParameter);
		if (variableName is null)
		{
			return Fail(ActionErrorCodes.InvalidParameter, AppStrings.Integrations.Http.Errors.NoTargetVariable());
		}

		if (!JsonEditor.TryParse(HttpActionValues.ReadRawText(parameters, JsonParameter), out var document))
		{
			return Fail(ActionErrorCodes.InvalidParameter, AppStrings.Integrations.Http.Errors.InvalidJsonInput());
		}

		var path = HttpActionValues.ReadText(parameters, PathParameter) ?? (blankPathIsRoot ? "$" : null);
		if (path is null || !JsonPathSyntax.TryParse(path, out var segments))
		{
			return Fail(ActionErrorCodes.InvalidParameter, AppStrings.Integrations.Http.Errors.InvalidJsonPath());
		}

		input = new JsonEditInput(document, segments, variableName);
		return null;
	}

	public static ActionResult? TryReadValue(IReadOnlyDictionary<string, object> parameters, out JsonNode? value)
	{
		value = null;
		var type = HttpActionValues.ReadText(parameters, ValueTypeParameter)?.ToLowerInvariant() ?? "text";
		var parsed = type switch
		{
			"text" => TextValue(parameters, out value),
			"number" => NumberValue(parameters, out value),
			"boolean" => BooleanValue(parameters, out value),
			"null" => true,
			"json" => JsonEditor.TryParse(HttpActionValues.ReadRawText(parameters, ValueParameter), out value),
			_ => false
		};

		return parsed
			? null
			: Fail(ActionErrorCodes.InvalidParameter, AppStrings.Integrations.Http.Errors.InvalidJsonValue());
	}

	public static async Task<ActionResult> SaveAsync(
		ActionExecutionContext context,
		HttpVariableAccessor variables,
		string variableName,
		JsonEditStatus status,
		VariableType type,
		Func<object> value)
	{
		if (status != JsonEditStatus.Succeeded)
		{
			return status switch
			{
				JsonEditStatus.NotFound => Fail(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Http.Errors.NoJsonValueAtPath()),
				JsonEditStatus.Blocked => Fail(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Http.Errors.JsonPathBlocked()),
				JsonEditStatus.NotContainer => Fail(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Http.Errors.JsonValueNotContainer()),
				JsonEditStatus.NotArray => Fail(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Http.Errors.JsonValueNotArray()),
				_ => Fail(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Http.Errors.JsonRootNotRemovable())
			};
		}

		var written = await HttpVariableWriter
			.WriteAsync(variables, variableName, UserVariableContext.From(context), type, value())
			.ConfigureAwait(false);

		return written
			? ActionResult.Success()
			: Fail(ActionErrorCodes.ProviderError, AppStrings.Integrations.Http.Errors.VariableSaveFailed());
	}

	private static bool TextValue(IReadOnlyDictionary<string, object> parameters, out JsonNode? value)
	{
		value = JsonValue.Create(HttpActionValues.ReadRawText(parameters, ValueParameter) ?? string.Empty);
		return true;
	}

	private static bool NumberValue(IReadOnlyDictionary<string, object> parameters, out JsonNode? value)
	{
		value = null;
		if (!JsonEditor.TryParse(HttpActionValues.ReadText(parameters, ValueParameter), out var node)
			|| node is not JsonValue number
			|| number.GetValueKind() != JsonValueKind.Number)
		{
			return false;
		}

		value = number;
		return true;
	}

	private static bool BooleanValue(IReadOnlyDictionary<string, object> parameters, out JsonNode? value)
	{
		value = null;
		var text = HttpActionValues.ReadText(parameters, ValueParameter);
		if (!bool.TryParse(text, out var flag))
		{
			return false;
		}

		value = JsonValue.Create(flag);
		return true;
	}

	private static ActionResult Fail(string code, LocalizedText message)
	{
		_logger.Warning("HTTP JSON edit failed: {Code}", code);
		return ActionResult.Failed(code, message);
	}
}
