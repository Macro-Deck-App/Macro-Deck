using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class SourceVisibilityActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string ModeShow = "show";
	internal const string ModeHide = "hide";
	internal const string ModeToggle = "toggle";

	private readonly Func<StreamlabsDesktopConnection?> _resolver;

	public SourceVisibilityActionDefinition(Func<StreamlabsDesktopConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-source-visibility";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.SourceVisibilityName();

	public LocalizedText Description =>
		AppStrings.Integrations.StreamlabsDesktop.Actions.SourceVisibilityDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SceneParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel(),
			required: true),
		ActionParameter.DynamicChoice(StreamlabsActionValues.SourceParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SourceLabel(),
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
					Value = ModeShow, Label = AppStrings.Integrations.StreamlabsDesktop.Params.ShowLabel()
				},
				new ActionParameterOption
				{
					Value = ModeHide, Label = AppStrings.Integrations.StreamlabsDesktop.Params.HideLabel()
				}
			],
			label: AppStrings.Integrations.StreamlabsDesktop.Params.ModeLabel(),
			defaultValue: ModeToggle)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> StreamlabsActionValues.SceneOrSourceOptionsAsync(_resolver(), context);

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

			if (StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SceneParameter) is not { } scene ||
				StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SourceParameter) is not { } source)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.NoSceneOrSourceSelected());
			}

			bool? visible = StreamlabsActionValues.ReadMode(context, ModeToggle) switch
			{
				ModeShow => true,
				ModeHide => false,
				_ => null
			};

			var result = await connection.SetSceneItemVisibleAsync(scene, source, visible).ConfigureAwait(false);
			return result.ToActionResult();
		}
	}
}
