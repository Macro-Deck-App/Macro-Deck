using System.Security;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Companion;
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
	private readonly Func<IUserVariableApi?> _userVariables;

	public TakeScreenshotAction(
		CompanionTargetResolver resolver,
		Func<IVariableApi?> variables,
		Func<IUserVariableApi?> userVariables)
	{
		_resolver = resolver;
		_variables = variables;
		_userVariables = userVariables;
	}

	public string Id => "take-screenshot";

	public LocalizedText Name => AppStrings.Integrations.Companion.Actions.TakeScreenshot.Name();

	public LocalizedText Description => AppStrings.Integrations.Companion.Actions.TakeScreenshot.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		CompanionTargetResolver.Parameter(),
		ActionParameter.Choice(ModeParameter,
			[
				new ActionParameterOption
					{ Value = DeckMode, Label = AppStrings.Integrations.Companion.Options.Deck() },
				new ActionParameterOption
					{ Value = FullMode, Label = AppStrings.Integrations.Companion.Options.FullScreen() }
			],
			AppStrings.Integrations.Companion.Params.ScreenshotMode(),
			defaultValue: DeckMode),
		ActionParameter.Folder(FolderParameter,
			AppStrings.Integrations.Adb.Actions.ScreenshotFolderLabel(),
			AppStrings.Integrations.Adb.Actions.ScreenshotFolderDescription(),
			true),
		ActionParameter.Autocomplete(FileNameVariableParameter,
			AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableLabel(),
			AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_resolver, _variables, _userVariables);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(_resolver.ConfigurationOptions());
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<TakeScreenshotAction>(CompanionIntegration.IntegrationId);

		private readonly CompanionTargetResolver _resolver;
		private readonly Func<IVariableApi?> _variables;
		private readonly Func<IUserVariableApi?> _userVariables;

		public Executor(
			CompanionTargetResolver resolver,
			Func<IVariableApi?> variables,
			Func<IUserVariableApi?> userVariables)
		{
			_resolver = resolver;
			_variables = variables;
			_userVariables = userVariables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!_resolver.TryResolveTarget(context.Parameters, out var deviceId, out var error))
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

			var (devices, notConnected) = await _resolver.ConnectAsync(deviceId, context.CancellationToken);
			if (devices is null)
			{
				return notConnected!;
			}

			var capability = mode == DeckMode
				? CompanionCapabilities.ScreenshotDeck
				: CompanionCapabilities.ScreenshotFull;
			var (unavailable, prompt, _) = CompanionCapabilities.Require(devices, deviceId, capability);
			if (unavailable is not null)
			{
				return unavailable;
			}

			var result = await devices.CaptureScreenshotAsync(deviceId,
				mode == DeckMode ? CompanionScreenshotMode.Deck : CompanionScreenshotMode.FullScreen,
				context.CancellationToken);
			if (result.Png is not { } png)
			{
				return CompanionCapabilities.Failed(result.Failure ?? CompanionDeviceFailure.Failed,
					capability,
					prompt);
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

			if (context.Parameters.GetValueOrDefault(FileNameVariableParameter) is string variableName
				&& !string.IsNullOrWhiteSpace(variableName))
			{
				await ActionVariableTarget.WriteAsync(_userVariables(),
					_variables(),
					variableName,
					UserVariableContext.From(context),
					VariableType.Text,
					path,
					_logger);
			}

			return ActionResult.Success();
		}
	}
}
