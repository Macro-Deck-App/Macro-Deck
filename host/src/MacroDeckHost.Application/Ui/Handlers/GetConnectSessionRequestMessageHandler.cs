using System.Text.Json;
using MacroDeckHost.Application.Connect;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Connect;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class GetConnectSessionRequestMessageHandler
	: IUiTransportMessageHandler<GetConnectSessionRequest, GetConnectSessionResponse>
{
	private readonly IConnectSessionService _sessionService;

	public GetConnectSessionRequestMessageHandler(IConnectSessionService sessionService)
		=> _sessionService = sessionService;

	// Must never trigger a refresh: this handler only reads the current snapshot. Refreshing here would
	// change state, broadcast a change notification, and re-enter this very handler through the client's
	// reaction to that notification.
	public ValueTask<GetConnectSessionResponse> Handle(GetConnectSessionRequest request,
		CancellationToken cancellationToken)
	{
		var snapshot = _sessionService.Current;
		var account = snapshot.Account;

		return new ValueTask<GetConnectSessionResponse>(new GetConnectSessionResponse
		{
			Status = CamelCase(snapshot.Status.ToString()),
			Connectivity = CamelCase(snapshot.Connectivity.ToString()),
			Account = account is null
				? null
				: new ConnectAccountPayload
				{
					Subject = account.Subject,
					DisplayName = account.DisplayName,
					AvatarAvailable = account.PictureUrl is not null,
					AvatarVersion = ConnectEndpoints.AvatarVersionOf(account.PictureUrl),
					CreatorUsername = account.CreatorUsername,
					Roles = account.Roles
				},
			OfflineSince = snapshot.OfflineSince,
			LastSuccessfulRefreshUtc = snapshot.LastSuccessfulRefreshUtc,
			Message = snapshot.Message,
			SignInFailure = snapshot.SignInFailure is { } failure ? CamelCase(failure.ToString()) : null,
			AccountManagementUrl = ConnectEndpoints.AccountManagementUrl
		});
	}

	private static string CamelCase(string value) => JsonNamingPolicy.CamelCase.ConvertName(value);
}
