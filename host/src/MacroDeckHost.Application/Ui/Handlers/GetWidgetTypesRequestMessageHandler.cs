using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetWidgetTypesRequestMessageHandler
	: IUiTransportMessageHandler<GetWidgetTypesRequest, GetWidgetTypesResponse>
{
	private readonly IWidgetTypeRegistry _widgetTypes;

	public GetWidgetTypesRequestMessageHandler(IWidgetTypeRegistry widgetTypes)
	{
		_widgetTypes = widgetTypes;
	}

	public ValueTask<GetWidgetTypesResponse> Handle(
		GetWidgetTypesRequest request,
		CancellationToken cancellationToken)
		=> ValueTask.FromResult(new GetWidgetTypesResponse
		{
			Types = WidgetTypeDtoMapper.MapToDto(_widgetTypes.All)
		});
}
