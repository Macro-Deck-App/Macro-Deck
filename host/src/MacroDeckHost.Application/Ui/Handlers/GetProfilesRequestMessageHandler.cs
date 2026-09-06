using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetProfilesRequestMessageHandler : IUiTransportMessageHandler<GetProfilesRequest, GetProfilesResponse>
{
	private readonly IProfileRegistry _profileRegistry;
	private readonly StartupReadiness _readiness;

	public GetProfilesRequestMessageHandler(IProfileRegistry profileRegistry, StartupReadiness readiness)
	{
		_profileRegistry = profileRegistry;
		_readiness = readiness;
	}

	public async ValueTask<GetProfilesResponse> Handle(GetProfilesRequest request, CancellationToken cancellationToken)
	{
		// Clients connect as soon as Kestrel listens; an early read must not
		// observe the still-empty profile cache as "no profiles".
		await _readiness.WhenReady.WaitAsync(cancellationToken);

		var response = new GetProfilesResponse();
		response.Profiles.AddRange(_profileRegistry.GetProfiles());

		return response;
	}
}
