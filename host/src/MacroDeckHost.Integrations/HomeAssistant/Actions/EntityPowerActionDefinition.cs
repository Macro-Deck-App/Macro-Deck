using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class EntityPowerActionDefinition : IDynamicOptionsActionDefinition, IStateProviderActionDefinition
{
	internal const string EntitiesParameterName = "entities";

	private const string ForwardingDomain = "homeassistant";

	private readonly string _service;
	private readonly Func<HomeAssistantConnection?> _resolver;

	public EntityPowerActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		string service,
		Func<HomeAssistantConnection?> resolver)
	{
		Id = id;
		Name = name;
		Description = description;
		_service = service;
		_resolver = resolver;
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.MultiSelect(EntitiesParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.EntityPower.EntitiesLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.EntityPower.EntitiesDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_service, _resolver);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantActionStates.Snapshot(_resolver(),
			HomeAssistantActionValues.ReadList(parameters.GetValueOrDefault(EntitiesParameterName))));

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
		=> Task.FromResult(HomeAssistantOptions.Entities(_resolver(), context.Filter));

	private sealed class Executor : IActionExecutor
	{
		private readonly string _service;
		private readonly Func<HomeAssistantConnection?> _resolver;

		public Executor(string service, Func<HomeAssistantConnection?> resolver)
		{
			_service = service;
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (!HomeAssistantServiceCall.TryConnect(_resolver, out var connection, out var rejection))
			{
				return rejection;
			}

			var entities = HomeAssistantActionValues.ReadList(context.Parameters, EntitiesParameterName);
			if (HomeAssistantServiceCall.Reject(connection, entities) is { } rejected)
			{
				return rejected;
			}

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				ForwardingDomain,
				_service,
				new Dictionary<string, object?>(StringComparer.Ordinal) { ["entity_id"] = entities },
				null,
				context.CancellationToken);
		}
	}
}
