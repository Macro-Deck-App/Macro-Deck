namespace MacroDeckHost.Application.Integrations.ConfigFlow;

public sealed record OAuthRegistration(string RedirectUri, string State);

public interface IOAuthCallbackCoordinator
{
	string RedirectUri { get; }

	OAuthRegistration Register(Guid flowId);

	string? GetCode(string state);

	Task<bool> HandleCallbackAsync(string state, string? code, string? errorCode, CancellationToken cancellationToken);

	void Release(string state);
}
