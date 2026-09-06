using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class RenameConfigEntryRequestMessageHandler
	: IUiTransportMessageHandler<RenameConfigEntryRequest, RenameConfigEntryResponse>
{
	private readonly IIntegrationConfigMutationCoordinator _mutations;

	public RenameConfigEntryRequestMessageHandler(IIntegrationConfigMutationCoordinator mutations)
	{
		_mutations = mutations;
	}

	public async ValueTask<RenameConfigEntryResponse> Handle(
		RenameConfigEntryRequest request,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(request.EntryId, out var entryId))
		{
			return Error("INVALID_ID", AppStrings.Errors.Config.InvalidEntryId(id: request.EntryId));
		}

		if (string.IsNullOrWhiteSpace(request.Title))
		{
			return Error("INVALID_TITLE", AppStrings.Errors.Config.TitleRequired());
		}

		var title = request.Title.Trim();

		var outcome = await _mutations.RenameAsync(request.IntegrationId, entryId, title, cancellationToken);
		return outcome.Success
			? new RenameConfigEntryResponse { Success = true }
			: Error("RENAME_REJECTED", outcome.Error);
	}

	private static RenameConfigEntryResponse Error(string code, MacroDeck.Localization.LocalizedText message)
		=> new() { Error = new TransportError { Code = code, Message = message } };
}
