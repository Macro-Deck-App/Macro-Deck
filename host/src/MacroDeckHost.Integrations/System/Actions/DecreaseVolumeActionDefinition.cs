using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class DecreaseVolumeActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly IVolumeService _volume;
	private readonly Func<AudioTarget, string?> _knownName;

	public DecreaseVolumeActionDefinition(IVolumeService volume, Func<AudioTarget, string?>? knownName = null)
	{
		_volume = volume;
		_knownName = knownName ?? (_ => null);
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
			defaultValue: 5),
		AudioDeviceParameter.Create()
	];

	public IActionExecutor CreateExecutor() => new VolumeStepExecutor(_volume, -1);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> AudioDeviceParameter.GetOptionsAsync(_volume, _knownName, context, cancellationToken);
}
