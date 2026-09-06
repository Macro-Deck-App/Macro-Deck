using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class SetVolumeActionDefinition : IActionDefinition
{
	private readonly IVolumeService _volume;

	public SetVolumeActionDefinition(IVolumeService volume)
	{
		_volume = volume;
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
			defaultValue: 50)
	];

	public IActionExecutor CreateExecutor() => new Executor(_volume);

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
			await _volume.SetVolumeAsync((float)(Math.Clamp(level, 0, 100) / 100), context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
