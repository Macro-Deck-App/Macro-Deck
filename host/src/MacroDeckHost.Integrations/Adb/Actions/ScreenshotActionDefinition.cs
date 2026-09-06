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
	private readonly Func<IAdbGateway?> _resolveGateway;
	private readonly AdbHealthTracker _health;
	private readonly VariableApiAccessor _variables;

	public ScreenshotActionDefinition(Func<IAdbGateway?> resolveGateway,
		AdbHealthTracker health,
		VariableApiAccessor variables)
	{
		_resolveGateway = resolveGateway;
		_health = health;
		_variables = variables;
	}

	public string Id => "screenshot";

	public LocalizedText Name => AppStrings.Integrations.Adb.Actions.ScreenshotName();

	public LocalizedText Description => AppStrings.Integrations.Adb.Actions.ScreenshotDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		AdbActionDefinition.DeviceParameter(AppStrings.Integrations.Adb.Params.DeviceDescription()),
		ActionParameter.Folder("folder",
			label: AppStrings.Integrations.Adb.Actions.ScreenshotFolderLabel(),
			description: AppStrings.Integrations.Adb.Actions.ScreenshotFolderDescription(),
			required: true),
		ActionParameter.Text("fileNameVariable",
			label: AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableLabel(),
			description: AppStrings.Integrations.Adb.Actions.ScreenshotSavePathVariableDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolveGateway, _health, _variables);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<ScreenshotActionDefinition>(AdbIntegration.IntegrationId);

		private readonly Func<IAdbGateway?> _resolveGateway;
		private readonly AdbHealthTracker _health;
		private readonly VariableApiAccessor _variables;

		public Executor(Func<IAdbGateway?> resolveGateway, AdbHealthTracker health, VariableApiAccessor variables)
		{
			_resolveGateway = resolveGateway;
			_health = health;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var gateway = _resolveGateway();
			if (gateway is null || !gateway.IsEnabled)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, AdbActionDefinition.DisabledMessage);
			}

			var folder = AdbActionValues.ReadString(context.Parameters, "folder");
			if (string.IsNullOrWhiteSpace(folder))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Adb.Errors.FolderRequired());
			}

			var device = AdbActionDefinition.ReadDevice(context.Parameters);
			var (result, png) = await gateway.CaptureScreenshotAsync(device, context.CancellationToken);
			_health.Record(result);

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
				await WriteVariableAsync(_variables.Current, fileNameVariable, path);
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
