using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Actions;

internal sealed class GetSourceVisibilityActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<StreamlabsDesktopConnection?> _resolver;
	private readonly VariableApiAccessor _variables;

	public GetSourceVisibilityActionDefinition(
		Func<StreamlabsDesktopConnection?> resolver,
		VariableApiAccessor variables)
	{
		_resolver = resolver;
		_variables = variables;
	}

	public string Id => "get-source-visibility";

	public LocalizedText Name => AppStrings.Integrations.StreamlabsDesktop.Actions.GetSourceVisibilityName();

	public LocalizedText Description =>
		AppStrings.Integrations.StreamlabsDesktop.Actions.GetSourceVisibilityDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(StreamlabsActionValues.SceneParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SceneLabel(),
			required: true),
		ActionParameter.DynamicChoice(StreamlabsActionValues.SourceParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SourceLabel(),
			required: true),
		ActionParameter.Text(StreamlabsActionValues.VariableParameter,
			label: AppStrings.Integrations.StreamlabsDesktop.Params.SaveToVariableLabel(),
			description: AppStrings.Integrations.StreamlabsDesktop.Params.SaveToVariableDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _variables);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> StreamlabsActionValues.SceneOrSourceOptionsAsync(_resolver(), context);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<StreamlabsDesktopConnection?> _resolver;
		private readonly VariableApiAccessor _variables;

		public Executor(Func<StreamlabsDesktopConnection?> resolver, VariableApiAccessor variables)
		{
			_resolver = resolver;
			_variables = variables;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_resolver() is not { } connection)
			{
				return StreamlabsActionResults.NotConnected;
			}

			if (StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SceneParameter) is not { } scene ||
				StreamlabsActionValues.ReadText(context, StreamlabsActionValues.SourceParameter) is not { } source ||
				StreamlabsActionValues.ReadText(context, StreamlabsActionValues.VariableParameter) is not { } variable)
			{
				return StreamlabsActionResults.MissingParameter(AppStrings.Integrations.StreamlabsDesktop.Errors
					.SceneSourceAndVariableRequired());
			}

			var visible = await connection.GetSceneItemVisibleAsync(scene, source).ConfigureAwait(false);
			if (visible is null)
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.StreamlabsDesktop.Errors.SourceNotFoundInScene(scene: scene,
						source: source));
			}

			return await StreamlabsVariableWriter
				.WriteAsync(_variables, variable, VariableType.Boolean, visible.Value)
				.ConfigureAwait(false)
				? ActionResult.Success()
				: ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.StreamlabsDesktop.Errors.CouldNotWriteVariable(variable: variable));
		}
	}
}
