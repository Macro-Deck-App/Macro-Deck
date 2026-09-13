using System.Security;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Companion.Actions;

internal sealed class TakeScreenshotAction : IDynamicOptionsActionDefinition
{
	internal const string ModeParameter = "mode";
	internal const string FolderParameter = "folder";
	internal const string FileNameVariableParameter = "fileNameVariable";
	internal const string DeckMode = "deck";
	internal const string FullMode = "full";

	private readonly CompanionTargetResolver _resolver;
	private readonly Func<IVariableApi?> _variables;

	public TakeScreenshotAction(CompanionTargetResolver resolver, Func<IVariableApi?> variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "take-screenshot";

	public LocalizedText Name => AppStrings.Integrations.Companion.Actions.TakeScreenshot.Name();

	public LocalizedText Description => AppStrings.Integrations.Companion.Actions.TakeScreenshot.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		CompanionTargetResolver.Parameter(),
		ActionParameter.Choice(ModeParameter,
			options:
			[
				new ActionParameterOption
					{ Value = DeckMode, Label = AppStrings.Integrations.Companion.Options.Deck() },
				new ActionParameterOption
					{ Value = FullMode, Label = AppStrings.Integrations.Companion.Options.FullScreen() }
			],
			label: AppStrings.Integrations.Companion.Params.ScreenshotMode(),
			defaultValue: DeckMode),
		ActionParameter.Folder(FolderParameter,
			label: AppStrings.Integrations.Adb.Actions.ScreenshotFolderLabel(),
			description: AppStrings.Integrations.Adb.Actions.ScreenshotFolderDescription(),
			required: true),
		ActionParameter.Text(FileNameVariableParameter,
			label: AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableLabel(),
			description: AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(_resolver.ConfigurationOptions());

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<TakeScreenshotAction>(CompanionIntegration.IntegrationId);

		private readonly CompanionTargetResolver _resolver;
		private readonly Func<IVariableApi?> _variables;

		public Executor(CompanionTargetResolver resolver, Func<IVariableApi?> variables)
		{
			_resolver = resolver;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolve(context.Parameters, out var deviceId, out var gateway, out var error))
			{
				return error;
			}

			var mode = context.Parameters.GetValueOrDefault(ModeParameter) switch
			{
				null => DeckMode,
				string value and (DeckMode or FullMode) => value,
				_ => null
			};
			if (mode is null)
			{
				return CompanionTargetResolver.InvalidParameter();
			}

			var folder = context.Parameters.GetValueOrDefault(FolderParameter) as string;
			if (string.IsNullOrWhiteSpace(folder))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Adb.Errors.FolderRequired());
			}

			var capability = mode == DeckMode ? CompanionCapabilities.ScreenshotDeck : CompanionCapabilities.ScreenshotFull;
			var (unavailable, prompt, _) = CompanionCapabilities.Require(gateway, deviceId, capability);
			if (unavailable is not null)
			{
				return unavailable;
			}

			var result = await gateway.RequestAsync(deviceId,
				new CompanionCommand(CompanionCommand.Screenshot, ScreenshotMode: mode),
				context.CancellationToken);
			if (result.Png is not { } png)
			{
				return CompanionCapabilities.Failed(result.Failure ?? CompanionCommandFailure.Failed, capability, prompt);
			}

			string path;
			try
			{
				Directory.CreateDirectory(folder);
				path = Path.Combine(folder, $"companion-screenshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png");
				await File.WriteAllBytesAsync(path, png, context.CancellationToken);
			}
			catch (Exception ex) when (ex is IOException
				or UnauthorizedAccessException
				or ArgumentException
				or NotSupportedException
				or SecurityException)
			{
				_logger.Warning(ex, "Could not save a Companion screenshot to the selected folder");
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Adb.Errors.ScreenshotSaveFailed());
			}

			if (context.Parameters.GetValueOrDefault(FileNameVariableParameter) is string variableName &&
				!string.IsNullOrWhiteSpace(variableName))
			{
				await WriteVariableAsync(_variables(), variableName, path);
			}

			return ActionResult.Success();
		}

		private static async Task WriteVariableAsync(IVariableApi? api, string variableName, string path)
		{
			if (api is null)
			{
				_logger.Warning("Cannot write the screenshot path: variable API unavailable");
				return;
			}

			try
			{
				var handle = await api.GetByNameAsync(variableName) ??
					await api.CreateAsync(variableName, VariableType.Text);
				await api.SetValueAsync(handle.Id, path);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Could not write the screenshot path to '{Variable}'", variableName);
			}
		}
	}
}
