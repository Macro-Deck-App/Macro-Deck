using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIntegrationIssuesRequestMessageHandler
	: IUiTransportMessageHandler<GetIntegrationIssuesRequest, GetIntegrationIssuesResponse>
{
	private readonly IIntegrationIssueService _issueService;

	public GetIntegrationIssuesRequestMessageHandler(IIntegrationIssueService issueService)
	{
		_issueService = issueService;
	}

	public async ValueTask<GetIntegrationIssuesResponse> Handle(
		GetIntegrationIssuesRequest request,
		CancellationToken cancellationToken)
	{
		var issues = await _issueService.GetIssuesAsync(request.IntegrationId, cancellationToken);
		return new GetIntegrationIssuesResponse
		{
			Issues = issues.Select(IntegrationIssueDto.From).ToList()
		};
	}
}
