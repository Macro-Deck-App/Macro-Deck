using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class IncreaseVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly IVolumeService _volume;
	private readonly Func<AudioTarget, string?> _knownName;

	public IncreaseVolumeActionDefinition(IVolumeService volume, Func<AudioTarget, string?>? knownName = null)
	{
		_volume = volume;
		_knownName = knownName ?? (_ => null);
	}

	public string Id => "increase-volume";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.IncreaseVolume.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.IncreaseVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Slider("amount",
			1,
			50,
			label: AppStrings.Integrations.System.Actions.VolumeAmount.Label(),
			step: 1,
			defaultValue: 5),
		AudioDeviceParameter.Create()
	];

	public IActionExecutor CreateExecutor() => new VolumeStepExecutor(_volume, 1);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> AudioDeviceParameter.GetOptionsAsync(_volume, _knownName, context, cancellationToken);
}

internal sealed class VolumeStepExecutor : IActionExecutor
{
	private readonly IVolumeService _volume;
	private readonly int _direction;

	public VolumeStepExecutor(IVolumeService volume, int direction)
	{
		_volume = volume;
		_direction = direction;
	}

	public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
	{
		var amount = SystemActionValues.ReadDouble(context.Parameters, "amount", 5);
		if (!AudioDeviceParameter.TryRead(context.Parameters.GetValueOrDefault(AudioDeviceParameter.Name),
				out var target) ||
			await _volume.GetVolumeAsync(target, context.CancellationToken) is not { } current ||
			!await _volume.SetVolumeAsync(target,
				Math.Clamp(current + _direction * (float)(amount / 100), 0f, 1f),
				context.CancellationToken))
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable,
				AppStrings.Integrations.System.Errors.VolumeUnavailable());
		}

		return ActionResult.Success();
	}
}
