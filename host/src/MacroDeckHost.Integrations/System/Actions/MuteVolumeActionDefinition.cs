using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.System.Actions;

internal sealed class MuteVolumeActionDefinition : IActionDefinition, IStateProviderActionDefinition
{
	private static readonly IReadOnlyList<ActionStateDefinition> _states =
	[
		new("unmuted", AppStrings.Integrations.System.Actions.MuteVolume.UnmutedLabel())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" }
		},
		new("muted", AppStrings.Integrations.System.Actions.MuteVolume.MutedLabel())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#c53030" }
		},
		new("unavailable", AppStrings.Integrations.System.Actions.MuteVolume.UnavailableLabel())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" }
		}
	];

	private readonly IVolumeService _volume;

	public MuteVolumeActionDefinition(IVolumeService volume)
	{
		_volume = volume;
	}

	public string Id => "mute-volume";
	public LocalizedText Name => AppStrings.Integrations.System.Actions.MuteVolume.Name();
	public LocalizedText Description => AppStrings.Integrations.System.Actions.MuteVolume.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public IActionExecutor CreateExecutor() => new Executor(_volume);

	public async Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
	{
		bool? muted;
		try
		{
			muted = _volume.IsSupported ? await _volume.GetMuteAsync(cancellationToken) : null;
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			muted = null;
		}

		var activeId = muted switch
		{
			true => "muted",
			false => "unmuted",
			null => "unavailable"
		};

		return new ActionStateSnapshot(_states, activeId);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly IVolumeService _volume;

		public Executor(IVolumeService volume)
		{
			_volume = volume;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (await _volume.GetMuteAsync(context.CancellationToken) is not { } muted)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.System.Errors.VolumeUnavailable());
			}

			await _volume.SetMuteAsync(!muted, context.CancellationToken);

			return ActionResult.Success(muted ? "unmuted" : "muted");
		}
	}
}
