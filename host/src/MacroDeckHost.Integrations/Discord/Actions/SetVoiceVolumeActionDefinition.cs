using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class SetVoiceVolumeActionDefinition : IActionDefinition
{
	internal const string VolumeParameter = "volume";

	private readonly Func<DiscordConnection?> _resolver;
	private readonly bool _output;
	private readonly double _max;

	public SetVoiceVolumeActionDefinition(Func<DiscordConnection?> resolver, bool output)
	{
		_resolver = resolver;
		_output = output;
		_max = output ? 200d : 100d;

		Parameters =
		[
			DiscordActionParameters.VolumeModeSelector(),
			ActionParameter.Slider(VolumeParameter,
				min: 0,
				max: _max,
				label: AppStrings.Integrations.Discord.Actions.SetVoiceVolume.VolumeLabel(),
				description: AppStrings.Integrations.Discord.Actions.SetVoiceVolume.VolumeDescription(),
				step: 1,
				defaultValue: 100d)
		];
	}

	public string Id => _output ? "set-output-volume" : "set-input-volume";

	public LocalizedText Name => _output
		? AppStrings.Integrations.Discord.Actions.SetVoiceVolume.OutputName()
		: AppStrings.Integrations.Discord.Actions.SetVoiceVolume.InputName();

	public LocalizedText Description => _output
		? AppStrings.Integrations.Discord.Actions.SetVoiceVolume.OutputDescription()
		: AppStrings.Integrations.Discord.Actions.SetVoiceVolume.InputDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(Id, _resolver, _output, _max);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetVoiceVolumeActionDefinition>(DiscordIntegration.IntegrationId);

		private readonly string _name;
		private readonly Func<DiscordConnection?> _resolver;
		private readonly bool _output;
		private readonly double _max;

		public Executor(string name, Func<DiscordConnection?> resolver, bool output, double max)
		{
			_name = name;
			_resolver = resolver;
			_output = output;
			_max = max;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Discord volume action skipped: Discord is not set up");
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Discord.Errors.NotSetUp());
			}

			var value = DiscordActionParameters.ReadNumber(context.Parameters.GetValueOrDefault(VolumeParameter));
			var mode = DiscordActionParameters.ReadMode(
				context.Parameters.GetValueOrDefault(DiscordActionParameters.ModeParameter));

			var state = connection.State;
			var current = _output ? state.OutputVolume : state.InputVolume;
			var target = mode switch
			{
				DiscordActionParameters.ModeIncrease => current + value,
				DiscordActionParameters.ModeDecrease => current - value,
				_ => value
			};

			var device = new DiscordVoiceDevicePatch { Volume = Math.Clamp(target, 0d, _max) };
			var patch = _output
				? new DiscordVoiceSettingsPatch { Output = device }
				: new DiscordVoiceSettingsPatch { Input = device };

			var result = await connection
				.SetVoiceSettingsAsync(patch, context.CancellationToken)
				.ConfigureAwait(false);

			return DiscordVoiceActionResults.ToActionResult(result, _name);
		}
	}
}
