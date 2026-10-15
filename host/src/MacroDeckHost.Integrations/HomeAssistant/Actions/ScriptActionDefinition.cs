using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.HomeAssistant.Connections;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class ScriptActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string EntityParameterName = "entity";
	internal const string DataParameterName = "data";

	private const string ScriptDomain = "script";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public ScriptActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "run-script";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.Script.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.Script.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(EntityParameterName,
			AppStrings.Integrations.HomeAssistant.Actions.Script.EntityLabel(),
			required: true),
		ActionParameter.Json(DataParameterName,
			AppStrings.Integrations.HomeAssistant.Actions.Script.VariablesLabel(),
			AppStrings.Integrations.HomeAssistant.Actions.Script.VariablesDescription())
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_resolver);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		return Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter, ScriptDomain));
	}

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

			var variables = HomeAssistantActionValues.ReadData(context.Parameters, DataParameterName);
			var data = variables is null
				? null
				: new Dictionary<string, object?>(StringComparer.Ordinal) { ["variables"] = variables };

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				ScriptDomain,
				"turn_on",
				HomeAssistantServiceCall.Target(entityId),
				data,
				context.CancellationToken);
		}
	}
}
