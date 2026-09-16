using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class SetVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly IVolumeService _volume;
	private readonly Func<AudioTarget, string?> _knownName;

	public SetVolumeActionDefinition(IVolumeService volume, Func<AudioTarget, string?>? knownName = null)
	{
		_volume = volume;
		_knownName = knownName ?? (_ => null);
	}

	public string Id => "set-volume";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.SetVolume.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.SetVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("level",
			0,
			100,
			label: AppStrings.Integrations.System.Actions.SetVolume.LevelLabel(),
			step: 1,
			defaultValue: 50),
		AudioDeviceParameter.Create()
	];

	public IActionExecutor CreateExecutor() => new Executor(_volume);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> AudioDeviceParameter.GetOptionsAsync(_volume, _knownName, context, cancellationToken);

	private sealed class Executor : IActionExecutor
	{
		private readonly IVolumeService _volume;

		public Executor(IVolumeService volume)
		{
			_volume = volume;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var level = SystemActionValues.ReadDouble(context.Parameters, "level", 50);
			if (!AudioDeviceParameter.TryRead(context.Parameters.GetValueOrDefault(AudioDeviceParameter.Name),
					out var target) ||
				!await _volume.SetVolumeAsync(target, (float)(Math.Clamp(level, 0, 100) / 100), context.CancellationToken))
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Errors.VolumeUnavailable());
			}

			return ActionResult.Success();
		}
	}
}
