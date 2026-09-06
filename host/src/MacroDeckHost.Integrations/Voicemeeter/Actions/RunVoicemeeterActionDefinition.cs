using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Voicemeeter.Actions;

internal sealed class RunVoicemeeterActionDefinition : IActionDefinition
{
	internal const string EditionParameter = "edition";
	internal const string AutoEdition = "auto";

	private static readonly VoicemeeterEdition[] _probeOrder =
	[
		VoicemeeterEdition.Potato,
		VoicemeeterEdition.Banana,
		VoicemeeterEdition.Standard
	];

	private readonly Func<VoicemeeterConnection?> _resolver;

	public RunVoicemeeterActionDefinition(Func<VoicemeeterConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "run-voicemeeter";

	public LocalizedText Name => AppStrings.Integrations.Voicemeeter.Actions.RunVoicemeeter.Name();

	public LocalizedText Description => AppStrings.Integrations.Voicemeeter.Actions.RunVoicemeeter.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(EditionParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = AutoEdition,
					Label = AppStrings.Integrations.Voicemeeter.Actions.RunVoicemeeter.InstalledEdition()
				},
				new ActionParameterOption { Value = nameof(VoicemeeterEdition.Standard), Label = "Voicemeeter" },
				new ActionParameterOption { Value = nameof(VoicemeeterEdition.Banana), Label = "Voicemeeter Banana" },
				new ActionParameterOption { Value = nameof(VoicemeeterEdition.Potato), Label = "Voicemeeter Potato" }
			],
			label: AppStrings.Integrations.Voicemeeter.Actions.RunVoicemeeter.EditionLabel(),
			defaultValue: AutoEdition)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<RunVoicemeeterActionDefinition>(VoicemeeterIntegration.IntegrationId);

		private readonly Func<VoicemeeterConnection?> _resolver;

		public Executor(Func<VoicemeeterConnection?> resolver)
		{
			_resolver = resolver;
		}

		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Voicemeeter run action skipped: the remote library is unavailable");
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Voicemeeter.Errors.RemoteApiUnavailable()));
			}

			if (connection.State.IsConnected)
			{
				_logger.Debug("Voicemeeter is already running");
				return ActionResult.SucceededTask;
			}

			var selected = VoicemeeterActionValues.ReadText(context.Parameters, EditionParameter) ?? AutoEdition;
			if (Enum.TryParse<VoicemeeterEdition>(selected, ignoreCase: true, out var edition) &&
				edition != VoicemeeterEdition.None)
			{
				return Task.FromResult(connection.RunVoicemeeter(edition)
					? ActionResult.Success()
					: ActionResult.Failed(ActionErrorCodes.ProviderError,
						AppStrings.Integrations.Voicemeeter.Errors.CouldNotStart()));
			}

			return Task.FromResult(RunInstalled(connection)
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Voicemeeter.Errors.NoEditionCouldBeStarted()));
		}

		private static bool RunInstalled(VoicemeeterConnection connection)
		{
			if (connection.Catalog.Edition != VoicemeeterEdition.None &&
				connection.RunVoicemeeter(connection.Catalog.Edition))
			{
				return true;
			}

			foreach (var candidate in _probeOrder)
			{
				if (connection.RunVoicemeeter(candidate))
				{
					return true;
				}
			}

			_logger.Warning("No Voicemeeter edition could be started");
			return false;
		}
	}
}
