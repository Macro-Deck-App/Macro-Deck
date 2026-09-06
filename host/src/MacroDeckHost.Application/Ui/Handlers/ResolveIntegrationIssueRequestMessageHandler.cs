using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class ResolveIntegrationIssueRequestMessageHandler
	: IUiTransportMessageHandler<ResolveIntegrationIssueRequest, ResolveIntegrationIssueResponse>
{
	private readonly IIntegrationIssueService _issueService;
	private readonly IIntegrationIssueBroadcastTrigger _issueTrigger;

	public ResolveIntegrationIssueRequestMessageHandler(
		IIntegrationIssueService issueService,
		IIntegrationIssueBroadcastTrigger issueTrigger)
	{
		_issueService = issueService;
		_issueTrigger = issueTrigger;
	}

	public async ValueTask<ResolveIntegrationIssueResponse> Handle(
		ResolveIntegrationIssueRequest request,
		CancellationToken cancellationToken)
	{
		var resolution = await _issueService.ResolveAsync(request.IntegrationId, request.IssueId, cancellationToken);
		if (resolution is null)
		{
			return new ResolveIntegrationIssueResponse
			{
				Success = false,
				Error = new TransportError
					{ Code = "NotFound", Message = AppStrings.Errors.Integrations.IssueNotFound() }
			};
		}

		_issueTrigger.RequestRefresh();

		return new ResolveIntegrationIssueResponse
		{
			Success = resolution.Success,
			Message = resolution.Message,
			FollowUp = resolution.FollowUp,
			Error = resolution.Success
				? null
				: new TransportError
				{
					Code = "ResolveFailed",
					Message = resolution.Message.IsEmpty
						? AppStrings.Errors.Integrations.IssueResolveFailed()
						: resolution.Message
				}
		};
	}
}
