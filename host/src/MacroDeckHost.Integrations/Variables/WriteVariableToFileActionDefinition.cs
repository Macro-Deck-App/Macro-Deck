using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Files;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Variables;

internal delegate Task<HostVariableValue?> VariableReader(
	string name,
	string? ownerWidgetId,
	CancellationToken cancellationToken);

internal sealed class WriteVariableToFileActionDefinition : IActionDefinition
{
	private readonly Func<VariableReader?> _reader;

	public WriteVariableToFileActionDefinition(Func<VariableReader?> reader)
	{
		_reader = reader;
	}

	public string Id => "write-variable-to-file";
	public LocalizedText Name => AppStrings.Integrations.Variables.Actions.WriteToFileName();
	public LocalizedText Description => AppStrings.Integrations.Variables.Actions.WriteToFileDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete("variable",
			label: AppStrings.Integrations.Variables.Actions.VariableLabel(),
			description: AppStrings.Integrations.Variables.Actions.WriteToFileVariableDescription(),
			optionsSourceId: VariableOptionsSourceIds.Variables,
			required: true),
		ActionParameter.File("file",
			label: AppStrings.Integrations.Variables.Actions.FileLabel(),
			description: AppStrings.Integrations.Variables.Actions.FileDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_reader);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<VariableReader?> _reader;

		public Executor(Func<VariableReader?> reader)
		{
			_reader = reader;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_reader() is not { } reader)
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

			var path = Read(context, "file")?.Trim();
			if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Variables.Errors.FilePathNotAbsolute());
			}

			var reading = await reader(name, context.OwnerWidgetId, context.CancellationToken);
			if (reading is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Variables.Errors.VariableNotFound());
			}

			if (!reading.Available)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Variables.Errors.VariableValueUnavailable());
			}

			try
			{
				await VariableFileText.WriteAsync(path, reading.Value, context.CancellationToken);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Variables.Errors.FileWriteFailed());
			}

			return ActionResult.Success();
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
	}
}
