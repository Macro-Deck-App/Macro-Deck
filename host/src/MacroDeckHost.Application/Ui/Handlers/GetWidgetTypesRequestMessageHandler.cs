using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetWidgetTypesRequestMessageHandler
	: IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse>
{
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IIntegrationRegistry _integrations;

	public GetWidgetTypesRequestMessageHandler(IWidgetTypeRegistry widgetTypes, IIntegrationRegistry integrations)
	{
		_widgetTypes = widgetTypes;
		_integrations = integrations;
	}

	public ValueTask<GetWidgetTypesResponse> Handle(
		GetWidgetTypesRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetWidgetTypesResponse
		{
			Types = WidgetTypeDtoMapper.MapToDto(_widgetTypes.All, _integrations)
		});
}
