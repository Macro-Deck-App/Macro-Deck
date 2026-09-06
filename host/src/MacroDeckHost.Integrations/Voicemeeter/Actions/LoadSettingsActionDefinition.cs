using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class LoadSettingsActionDefinition : IActionDefinition
{
	internal const string FileParameter = "file";

	private readonly Func<VoicemeeterConnection?> _resolver;

	public LoadSettingsActionDefinition(Func<VoicemeeterConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "load-settings";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.LoadSettings.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.LoadSettings.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.File(FileParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.LoadSettings.FileLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.LoadSettings.FileDescription(),
			fileExtensions: ["xml"],
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<LoadSettingsActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(Func<VoicemeeterConnection?> resolver)
		{
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var file = VoicemeeterActionValues.ReadText(context.Parameters, FileParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter load-settings action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (file is null)
			{
				_logger.Warning("Voicemeeter load-settings action skipped: no file selected");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoSettingsFileSelected()));
			}

			connection.SetParameter(VoicemeeterParameters.Command("Load"), file);
			return ActionResult.SucceededTask;
		}
	}
}
