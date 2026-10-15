using System.Security;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.Adb.Actions;

internal sealed class ScreenshotActionDefinition : IActionDefinition
{
	private readonly AdbActionServices _services;

	public ScreenshotActionDefinition(AdbActionServices services)
	{
		_services = services;
	}

	public string Id => "screenshot";

	public LocalizedText Name => AppStrings.Integrations.Adb.Actions.ScreenshotName();

	public LocalizedText Description => AppStrings.Integrations.Adb.Actions.ScreenshotDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		AdbActionDefinition.DeviceParameter(AppStrings.Integrations.Adb.Params.DeviceDescription()),
		ActionParameter.Folder("folder",
			AppStrings.Integrations.Adb.Actions.ScreenshotFolderLabel(),
			AppStrings.Integrations.Adb.Actions.ScreenshotFolderDescription(),
			true),
		ActionParameter.Autocomplete("fileNameVariable",
			AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableLabel(),
			AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_services);
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<ScreenshotActionDefinition>(AdbIntegration.IntegrationId);

		private readonly AdbActionServices _services;

		public Executor(AdbActionServices services)
		{
			_services = services;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!AdbActionDefinition.IsAvailable(_services.Devices(), out var manager))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, AdbActionDefinition.DisabledMessage);
			}

			var folder = AdbActionValues.ReadString(context.Parameters, "folder");
			if (string.IsNullOrWhiteSpace(folder))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Adb.Errors.FolderRequired());
			}

			var target = new AdbTarget(manager,
				_services.Tracker(),
				AdbActionDefinition.ReadDevice(context.Parameters));
			var (result, png) =
				await target.RunAsync(device => device.CaptureScreenshotAsync(context.CancellationToken));
			_services.Health.Record(result);

			if (!result.Success)
			{
				return AdbActionDefinition.MapResult(result);
			}

			if (png is null || png.Length == 0)
			{
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Adb.Errors.NoScreenshotData());
			}

			string path;
			try
			{
				Directory.CreateDirectory(folder);
				var fileName = $"adb-screenshot-{DateTime.Now:yyyyMMdd-HHmmss-fff}.png";
				path = Path.Combine(folder, fileName);
				await File.WriteAllBytesAsync(path, png, context.CancellationToken);
			}
			catch (Exception ex) when (ex is IOException
				or UnauthorizedAccessException
				or ArgumentException
				or NotSupportedException
				or SecurityException)
			{
				_logger.Warning(ex, "Could not save an ADB screenshot to the selected folder");
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Adb.Errors.ScreenshotSaveFailed());
			}

			var fileNameVariable = AdbActionValues.ReadString(context.Parameters, "fileNameVariable");
			if (!string.IsNullOrWhiteSpace(fileNameVariable))
			{
				await ActionVariableTarget.WriteAsync(_services.Variables.UserVariables,
					_services.Variables.Current,
					fileNameVariable,
					UserVariableContext.From(context),
					VariableType.Text,
					path,
					_logger);
			}

			return ActionResult.Success();
		}
	}
}
