using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class DeleteConfigEntryRequestMessageHandler
	: IUiTransportMessageHandler<DeleteConfigEntryRequest, DeleteConfigEntryResponse>
{
	private readonly IIntegrationConfigMutationCoordinator _mutations;

	public DeleteConfigEntryRequestMessageHandler(IIntegrationConfigMutationCoordinator mutations)
	{
		_mutations = mutations;
	}

	public async ValueTask<DeleteConfigEntryResponse> Handle(
		DeleteConfigEntryRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.EntryId, out var entryId))
		{
			return new DeleteConfigEntryResponse
			{
				Error = new TransportError
				{
					Code = "INVALID_ID",
					Message = AppStrings.Errors.Config.InvalidEntryId(id: request.EntryId)
				}
			};
		}

		var outcome = await _mutations.DeleteAsync(request.IntegrationId,
			entryId,
			request.Confirmed,
			cancellationToken);
		return outcome.Success
			? new DeleteConfigEntryResponse { Success = true }
			: new DeleteConfigEntryResponse
			{
				Error = new TransportError { Code = "DELETE_REJECTED", Message = outcome.Error }
			};
	}
}
