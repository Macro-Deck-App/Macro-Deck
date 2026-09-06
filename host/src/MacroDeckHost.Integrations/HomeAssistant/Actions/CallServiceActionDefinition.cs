using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal sealed class CallServiceActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string DomainParameterName = "domain";
	internal const string ServiceParameterName = "service";
	internal const string EntitiesParameterName = "entities";
	internal const string AreasParameterName = "areas";
	internal const string DevicesParameterName = "devices";
	internal const string DataParameterName = "data";

	private readonly Func<HomeAssistantConnection?> _resolver;

	public CallServiceActionDefinition(Func<HomeAssistantConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "call-service";

	public LocalizedText Name => AppStrings.Integrations.HomeAssistant.Actions.CallService.Name();

	public LocalizedText Description => AppStrings.Integrations.HomeAssistant.Actions.CallService.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(DomainParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.CallService.DomainLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.CallService.DomainDescription(),
			required: true),
		ActionParameter.Autocomplete(ServiceParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.CallService.ServiceLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.CallService.ServiceDescription(),
			required: true),
		ActionParameter.MultiSelect(EntitiesParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.CallService.EntitiesLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.CallService.EntitiesDescription()),
		ActionParameter.MultiSelect(AreasParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.CallService.AreasLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.CallService.AreasDescription()),
		ActionParameter.MultiSelect(DevicesParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.CallService.DevicesLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.CallService.DevicesDescription()),
		ActionParameter.Json(DataParameterName,
			label: AppStrings.Integrations.HomeAssistant.Actions.CallService.DataLabel(),
			description: AppStrings.Integrations.HomeAssistant.Actions.CallService.DataDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _resolver();
		var domain = context.CurrentParameters.GetValueOrDefault(DomainParameterName) as string;

		var result = context.ParameterName switch
		{
			DomainParameterName => HomeAssistantOptions.Domains(connection, context.Filter),
			ServiceParameterName => HomeAssistantOptions.Services(connection, context.Filter, domain),
			EntitiesParameterName => HomeAssistantOptions.Entities(connection, context.Filter, domain),
			AreasParameterName => HomeAssistantOptions.Areas(connection, context.Filter),
			DevicesParameterName => HomeAssistantOptions.Devices(connection, context.Filter),
			_ => HomeAssistantOptions.Values([], context.Filter)
		};

		return Task.FromResult(result);
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

			var domain = HomeAssistantActionValues.ReadText(context.Parameters, DomainParameterName);
			var service = HomeAssistantActionValues.ReadText(context.Parameters, ServiceParameterName);
			if (domain is null || service is null)
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.HomeAssistant.Errors.NoServiceSelected());
			}

			var entities = HomeAssistantActionValues.ReadList(context.Parameters, EntitiesParameterName);
			if (entities.Count > 0 && HomeAssistantServiceCall.Reject(connection, entities) is { } rejected)
			{
				return rejected;
			}

			var areas = HomeAssistantActionValues.ReadList(context.Parameters, AreasParameterName);
			var devices = HomeAssistantActionValues.ReadList(context.Parameters, DevicesParameterName);
			var data = HomeAssistantActionValues.ReadData(context.Parameters, DataParameterName);

			return await HomeAssistantServiceCall.ExecuteAsync(connection,
				domain,
				service,
				BuildTarget(entities, areas, devices),
				data,
				context.CancellationToken);
		}

		private static Dictionary<string, object?>? BuildTarget(
			IReadOnlyList<string> entities,
			IReadOnlyList<string> areas,
			IReadOnlyList<string> devices)
		{
			var target = new Dictionary<string, object?>(StringComparer.Ordinal);
			if (entities.Count > 0)
			{
				target["entity_id"] = entities;
			}

			if (areas.Count > 0)
			{
				target["area_id"] = areas;
			}

			if (devices.Count > 0)
			{
				target["device_id"] = devices;
			}

			return target.Count > 0 ? target : null;
		}
	}
}
