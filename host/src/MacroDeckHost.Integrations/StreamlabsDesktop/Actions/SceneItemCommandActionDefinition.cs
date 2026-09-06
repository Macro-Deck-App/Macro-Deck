using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class SceneItemCommandActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string CommandParameter = "command";
	internal const string DegreesParameter = "degrees";
	internal const string CommandRotate = "rotate";

	private static readonly IReadOnlyDictionary<string, string> _methods =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["fit-to-screen"] = "fitToScreen",
			["center-on-screen"] = "centerOnScreen",
			["stretch-to-screen"] = "stretchToScreen",
			["reset-transform"] = "resetTransform",
			["flip-x"] = "flipX",
			["flip-y"] = "flipY",
			[CommandRotate] = CommandRotate
		};

	private readonly Func<StreamlabsDesktopConnection?> _resolver;

	public SceneItemCommandActionDefinition(Func<StreamlabsDesktopConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "scene-item-command";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.SceneItemCommandName();

	public LocalizedText Description =>
		AppStrings.Integrations.StreamlabsDesktop.Actions.SceneItemCommandDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SceneParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel(),
			required: true),
		ActionParameter.DynamicChoice(StreamlabsActionValues.SourceParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SourceLabel(),
			required: true),
		ActionParameter.Choice(CommandParameter,
			options:
			[
				new ActionParameterOption
				{
					Value = "fit-to-screen",
					Label = AppStrings.Integrations.StreamlabsDesktop.Params.FitToScreenLabel()
				},
				new ActionParameterOption
				{
					Value = "center-on-screen",
					Label = AppStrings.Integrations.StreamlabsDesktop.Params.CenterOnScreenLabel()
				},
				new ActionParameterOption
				{
					Value = "stretch-to-screen",
					Label = AppStrings.Integrations.StreamlabsDesktop.Params.StretchToScreenLabel()
				},
				new ActionParameterOption
				{
					Value = "reset-transform",
					Label = AppStrings.Integrations.StreamlabsDesktop.Params.ResetTransformLabel()
				},
				new ActionParameterOption
				{
					Value = "flip-x", Label = AppStrings.Integrations.StreamlabsDesktop.Params.FlipHorizontallyLabel()
				},
				new ActionParameterOption
				{
					Value = "flip-y", Label = AppStrings.Integrations.StreamlabsDesktop.Params.FlipVerticallyLabel()
				},
				new ActionParameterOption
				{
					Value = CommandRotate, Label = AppStrings.Integrations.StreamlabsDesktop.Params.RotateLabel()
				}
			],
			label: AppStrings.Integrations.StreamlabsDesktop.Params.TransformActionLabel(),
			defaultValue: "fit-to-screen"),
		ActionParameter.Number(DegreesParameter,
				label: AppStrings.Integrations.StreamlabsDesktop.Params.DegreesLabel(),
				min: -360,
				max: 360,
				defaultValue: 90)
			.OnlyWhen(CommandParameter, CommandRotate)
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

			var command = context.Parameters.GetValueOrDefault(CommandParameter) as string ?? "fit-to-screen";
			if (!_methods.TryGetValue(command, out var method))
			{
				return StreamlabsActionResults.MissingParameter(
					AppStrings.Integrations.StreamlabsDesktop.Errors.NotASceneItemAction(command: command));
			}

			double? degrees = null;
			if (string.Equals(command, CommandRotate, StringComparison.Ordinal))
			{
				degrees = StreamlabsActionValues.ReadNumber(context.Parameters.GetValueOrDefault(DegreesParameter));
				if (degrees is null)
				{
					return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
						.RotationNotANumber());
				}
			}

			var result = await connection
				.RunSceneItemCommandAsync(scene, source, method, degrees)
				.ConfigureAwait(false);

			return result.ToActionResult();
		}
	}
}
