using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class RunScriptActionDefinition : IActionDefinition
{
	internal const string ScriptParameter = "script";

	private readonly Func<VoicemeeterConnection?> _resolver;

	public RunScriptActionDefinition(Func<VoicemeeterConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "run-script";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.RunScript.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.RunScript.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.MultilineText(ScriptParameter,
			label: AppStrings.Integrations.Voicemeeter.Actions.RunScript.ScriptLabel(),
			description: AppStrings.Integrations.Voicemeeter.Actions.RunScript.ScriptDescription(),
			placeholder: "Strip[0].Mute = 1\nBus[0].Gain = -6.0",
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<RunScriptActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(Func<VoicemeeterConnection?> resolver)
		{
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			var script = VoicemeeterActionValues.ReadText(context.Parameters, ScriptParameter);

			if (connection is null)
			{
				_logger.Warning("Voicemeeter run-script action skipped: Voicemeeter is not running");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.NotRunning()));
			}

			if (script is null)
			{
				_logger.Warning("Voicemeeter run-script action skipped: the script is empty");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Voicemeeter.Errors.NoScriptEntered()));
			}

			connection.RunScript(script);
			return ActionResult.SucceededTask;
		}
	}
}
