using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetEventDefinitionsRequestMessageHandler
	: IUiTransportMessageHandler<GetEventDefinitionsRequest, GetEventDefinitionsResponse>
{
	private readonly IEventRegistry _registry;

	public GetEventDefinitionsRequestMessageHandler(IEventRegistry registry)
	{
		_registry = registry;
	}

	public ValueTask<GetEventDefinitionsResponse> Handle(
		GetEventDefinitionsRequest request,
		CancellationToken cancellationToken)
	{
		var response = new GetEventDefinitionsResponse();

		foreach (var descriptor in _registry.GetDefinitions())
		{
			var definition = descriptor.Definition;
			response.Events.Add(new EventDefinitionDto
			{
				Id = descriptor.Id.ToString(),
				ProviderId = descriptor.ProviderId,
				ProviderName = descriptor.ProviderName,
				IsIntegration = descriptor.IsIntegrationProvider,
				Name = definition.Name,
				Description = definition.Description,
				Category = definition.Category,
				IconName = definition.IconName,
				DeliveryKind = definition.DeliveryKind == EventDeliveryKind.Scheduled ? "scheduled" : "push",
				ConfigurationParameters = definition.ConfigurationParameters
					.Select(ActionParameterDefMapper.Map)
					.ToList(),
				PayloadParameters = definition.PayloadParameters.Select(ActionParameterDefMapper.Map).ToList()
			});
		}

		return ValueTask.FromResult(response);
	}
}
