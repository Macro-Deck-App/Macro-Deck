using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class SceneActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string TransitionParameterName = "transition";

	private const string SceneDomain = "scene";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public SceneActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "activate-scene";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.Scene.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.Scene.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Scene.EntityLabel(),
			required: true),
		ActionParameter.Number(TransitionParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.Scene.TransitionLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.Scene.TransitionDescription(),
			min: 0,
			max: 300)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter, SceneDomain));

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<HomeAssistantConnection?> _resolver;

		public Executor(Func<HomeAssistantConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!HomeAssistantServiceCall.TryConnect(_resolver, out var connection, out var rejection))
			{
				return rejection;
			}

			if (!HomeAssistantServiceCall.TryEntity(connection,
				HomeAssistantActionValues.ReadText(context.Parameters, EntityParameterName),
				out var entityId,
				out var rejected))
			{
				return rejected;
			}

			var data = new Dictionary<string, object?>(StringComparer.Ordinal);
			if (HomeAssistantActionValues.ReadNumber(context.Parameters, TransitionParameterName, 0, 300) is
				{ } transition)
			{
				data["transition"] = transition;
			}

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				SceneDomain,
				"turn_on",
				HomeAssistantServiceCall.Target(entityId),
				data.Count > 0 ? data : null,
				context.CancellationToken);
		}
	}
}
