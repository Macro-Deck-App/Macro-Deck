using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetConfigEntriesRequestMessageHandler
	: IUiTransportMessageHandler<GetConfigEntriesRequest, GetConfigEntriesResponse>
{
	private readonly IIntegrationConfigMutationCoordinator _mutations;

	public GetConfigEntriesRequestMessageHandler(IIntegrationConfigMutationCoordinator mutations)
	{
		_mutations = mutations;
	}

	public async ValueTask<GetConfigEntriesResponse> Handle(
		GetConfigEntriesRequest request,
		CancellationToken cancellationToken)
	{
		var entries = await _mutations.DescribeAsync(request.IntegrationId, cancellationToken);

		return new GetConfigEntriesResponse
		{
			Entries = entries
				.Select(e => new ConfigEntryDto
				{
					Id = e.Id.ToString(),
					Title = e.Title,
					CreatedAt = e.CreatedAt,
					Status = e.Status.ToString(),
					Usable = e.Usable
				})
				.ToList()
		};
	}
}
