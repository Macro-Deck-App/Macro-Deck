using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Variables;

internal sealed class SetVariableActionDefinition : IActionDefinition
{
	private const string UserVariablesOptionsSource = "macrodeck.user-variables";

	private const string OperationSet = "set";
	private const string OperationAdd = "add";
	private const string OperationToggle = "toggle";
	private const string OperationAppend = "append";

	private readonly Func<IUserVariableApi?> _variables;

	public SetVariableActionDefinition(Func<IUserVariableApi?> variables)
	{
		_variables = variables;
	}

	public string Id => "set-variable";
	public LocalizedText Name => AppStrings.Integrations.Variables.Actions.SetVariableName();
	public LocalizedText Description => AppStrings.Integrations.Variables.Actions.SetVariableDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete("variable",
			label: AppStrings.Integrations.Variables.Actions.VariableLabel(),
			description: AppStrings.Integrations.Variables.Actions.VariableDescription(),
			optionsSourceId: UserVariablesOptionsSource,
			required: true),
		ActionParameter.Choice("operation",
			options:
			[
				new ActionParameterOption
					{ Value = OperationSet, Label = AppStrings.Integrations.Variables.Actions.OperationSetTo() },
				new ActionParameterOption
					{ Value = OperationAdd, Label = AppStrings.Integrations.Variables.Actions.OperationAdd() },
				new ActionParameterOption
					{ Value = OperationToggle, Label = AppStrings.Integrations.Variables.Actions.OperationToggle() },
				new ActionParameterOption
					{ Value = OperationAppend, Label = AppStrings.Integrations.Variables.Actions.OperationAppend() }
			],
			label: AppStrings.Integrations.Variables.Actions.OperationLabel(),
			description: AppStrings.Integrations.Variables.Actions.OperationDescription(),
			defaultValue: OperationSet),
		ActionParameter.Text("value",
				label: AppStrings.Integrations.Variables.Actions.ValueLabel(),
				description: AppStrings.Integrations.Variables.Actions.ValueDescription())
			.OnlyWhen("operation", OperationSet, OperationAdd, OperationAppend)
	];

	public IActionExecutor CreateExecutor() => new Executor(_variables);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<IUserVariableApi?> _variables;

		public Executor(Func<IUserVariableApi?> variables)
		{
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var variables = _variables();
			if (variables is null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Variables.Errors.VariablesUnavailable());
			}

			var name = Read(context, "variable");
			if (string.IsNullOrWhiteSpace(name))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Variables.Errors.NoVariableSelected());
			}

			var raw = Read(context, "operation");
			if (!TryParseOperation(raw, out var operation))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Variables.Errors.UnknownOperation(operation: raw ?? string.Empty));
			}

			var result = await variables.ApplyAsync(name,
				context.OwnerWidgetId,
				operation,
				Read(context, "value"),
				context.CancellationToken);

			return result.Status switch
			{
				UserVariableWriteStatus.Applied => ActionResult.Success(),
				UserVariableWriteStatus.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
					result.Message is { } notFoundMessage
						? notFoundMessage
						: AppStrings.Integrations.Variables.Errors.VariableNotFound()),
				UserVariableWriteStatus.NotEditable => ActionResult.Failed(ActionErrorCodes.PermissionDenied,
					result.Message is { } notEditableMessage
						? notEditableMessage
						: AppStrings.Integrations.Variables.Errors.VariableReadOnly()),
				_ => ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					result.Message is { } invalidMessage
						? invalidMessage
						: AppStrings.Integrations.Variables.Errors.ValueDoesNotFit())
			};
		}

		private static string? Read(ActionExecutionContext context, string name)
		{
			if (!context.Parameters.TryGetValue(name, out var value))
			{
				return null;
			}

			return value is IFormattable formattable
				? formattable.ToString(null, CultureInfo.InvariantCulture)
				: value.ToString();
		}

		private static bool TryParseOperation(string? raw, out UserVariableOperation operation)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				operation = UserVariableOperation.Set;
				return true;
			}

			switch (raw.Trim().ToLowerInvariant())
			{
				case OperationSet:
					operation = UserVariableOperation.Set;
					return true;
				case OperationAdd:
					operation = UserVariableOperation.Add;
					return true;
				case OperationToggle:
					operation = UserVariableOperation.Toggle;
					return true;
				case OperationAppend:
					operation = UserVariableOperation.Append;
					return true;
				default:
					operation = UserVariableOperation.Set;
					return false;
			}
		}
	}
}
