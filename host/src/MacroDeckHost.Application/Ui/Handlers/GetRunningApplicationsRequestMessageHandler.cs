using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using WireRunningApplication = MacroDeckHost.Application.Ui.Transport.Messages.System.RunningApplication;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetRunningApplicationsRequestMessageHandler
	: IUiTransportMessageHandler<GetRunningApplicationsRequest, GetRunningApplicationsResponse>
{
	private readonly IRunningApplicationCatalog _catalog;

	public GetRunningApplicationsRequestMessageHandler(IRunningApplicationCatalog catalog)
	{
		_catalog = catalog;
	}

	public async ValueTask<GetRunningApplicationsResponse> Handle(GetRunningApplicationsRequest request,
		CancellationToken cancellationToken)
	{
		var applications = await _catalog.GetAsync(request.Filter, cancellationToken);

		return new GetRunningApplicationsResponse
		{
			Applications = applications.Select(app => new WireRunningApplication
				{
					Identity = app.Identity,
					IdentityKind = app.IdentityKind,
					Label = app.Label
				})
				.ToList()
		};
	}
}
