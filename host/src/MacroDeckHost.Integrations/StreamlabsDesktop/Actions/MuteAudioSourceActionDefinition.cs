using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class MuteAudioSourceActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string ModeMute = "mute";
	internal const string ModeUnmute = "unmute";
	internal const string ModeToggle = "toggle";

	private readonly Func<StreamlabsDesktopConnection?> _resolver;

	public MuteAudioSourceActionDefinition(Func<StreamlabsDesktopConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-audio-mute";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.MuteAudioSourceName();

	public LocalizedText Description =>
		AppStrings.Integrations.StreamlabsDesktop.Actions.MuteAudioSourceDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SourceParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.AudioSourceLabel(),
			required: true),
		ActionParameter.Choice(StreamlabsActionValues.ModeParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = ModeToggle, Label = AppStrings.Integrations.StreamlabsDesktop.Params.ToggleLabel()
				},
				new ActionParameterOption
				{
					Value = ModeMute, Label = AppStrings.Integrations.StreamlabsDesktop.Params.MuteLabel()
				},
				new ActionParameterOption
				{
					Value = ModeUnmute, Label = AppStrings.Integrations.StreamlabsDesktop.Params.UnmuteLabel()
				}
			],
			label: AppStrings.Integrations.StreamlabsDesktop.Params.ModeLabel(),
			defaultValue: ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (_resolver() is not { } connection || context.ParameterName != StreamlabsActionValues.SourceParameter)
		{
			return StreamlabsActionValues.Options([]);
		}

		return StreamlabsActionValues.Options(await connection.GetAudioSourceNamesAsync().ConfigureAwait(false));
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<StreamlabsDesktopConnection?> _resolver;

		public Executor(Func<StreamlabsDesktopConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_resolver() is not { } connection)
			{
				return StreamlabsActionResults.NotConnected;
			}

			if (StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SourceParameter) is not { } source)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.NoAudioSourceSelected());
			}

			bool? muted = StreamlabsActionValues.ReadMode(context, ModeToggle) switch
			{
				ModeMute => true,
				ModeUnmute => false,
				_ => null
			};

			var result = await connection.SetAudioMutedAsync(source, muted).ConfigureAwait(false);
			return result.ToActionResult();
		}
	}
}
