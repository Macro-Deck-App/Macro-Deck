using MacroDeckHost.Application.Email;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIntegrationEmailInboxesRequestMessageHandler
	: IUiTransportMessageHandler<GetIntegrationEmailInboxesRequest, GetIntegrationEmailInboxesResponse>
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IEmailInboxCache _cache;

	public GetIntegrationEmailInboxesRequestMessageHandler(IIntegrationRegistry integrations, IEmailInboxCache cache)
	{
		_integrations = integrations;
		_cache = cache;
	}

	public ValueTask<GetIntegrationEmailInboxesResponse> Handle(
		GetIntegrationEmailInboxesRequest request,
		CancellationToken cancellationToken)
	{
		var integration = IntegrationCapabilityData.Find(_integrations, request.IntegrationId);
		if (integration is null)
		{
			return ValueTask.FromResult(new GetIntegrationEmailInboxesResponse());
		}

		return ValueTask.FromResult(new GetIntegrationEmailInboxesResponse
		{
			Found = true,
			Available = IntegrationCapabilityData.IsAvailable(_integrations, integration),
			Inboxes =
			[
				.. _cache.Snapshot.Inboxes
					.Where(state => state.Inbox.IntegrationId == integration.Id)
					.Select(state => new IntegrationEmailInboxDto
					{
						Ref = state.Ref,
						DisplayName = state.Inbox.DisplayName,
						Address = state.Inbox.Address,
						Status = state.Status,
						StatusMessage = state.StatusMessage,
						UnreadCount = state.UnreadCount,
						TotalCount = state.TotalCount,
						SyncedAt = state.SyncedAt,
						IsStale = state.IsStale,
						Messages =
						[
							.. state.Latest.Select(message => new IntegrationEmailMessageDto
							{
								Id = message.Id,
								SenderName = message.SenderName,
								SenderAddress = message.SenderAddress,
								Subject = message.Subject,
								Preview = message.Preview,
								ReceivedAt = message.ReceivedAt,
								IsRead = message.IsRead
							})
						]
					})
			]
		});
	}
}
