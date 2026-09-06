using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class DecreaseVolumeActionDefinition : IActionDefinition
{
	private readonly IVolumeService _volume;

	public DecreaseVolumeActionDefinition(IVolumeService volume)
	{
		_volume = volume;
	}

	public string Id => "decrease-volume";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.DecreaseVolume.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.DecreaseVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("amount",
			1,
			50,
			label: AppStrings.Integrations.System.Actions.VolumeAmount.Label(),
			step: 1,
			defaultValue: 5)
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
			var amount = SystemActionValues.ReadDouble(context.Parameters, "amount", 5);
			if (await _volume.GetVolumeAsync(context.CancellationToken) is not { } current)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Errors.VolumeUnavailable());
			}

			var target = Math.Clamp(current - (float)(amount / 100), 0f, 1f);
			await _volume.SetVolumeAsync(target, context.CancellationToken);

			return ActionResult.Success();
		}
	}
}
