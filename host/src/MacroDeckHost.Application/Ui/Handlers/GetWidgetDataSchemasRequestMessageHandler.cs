using System.Text.Json;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetWidgetDataSchemasRequestMessageHandler
	: IUiTransportMessageHandler<GetWidgetDataSchemasRequest, GetWidgetDataSchemasResponse>
{
	private readonly IWidgetDataSchemaProvider _schemas;

	public GetWidgetDataSchemasRequestMessageHandler(IWidgetDataSchemaProvider schemas)
	{
		_schemas = schemas;
	}

	public ValueTask<GetWidgetDataSchemasResponse> Handle(
		GetWidgetDataSchemasRequest request,
		CancellationToken cancellationToken)
	{
		return ValueTask.FromResult(new GetWidgetDataSchemasResponse
		{
			Success = true,
			Schemas = new Dictionary<string, JsonElement>(_schemas.All())
		});
	}
}
