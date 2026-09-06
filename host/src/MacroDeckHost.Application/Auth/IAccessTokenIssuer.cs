using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Auth;

public record AccessToken(string Token, DateTime ExpiresAt);

public interface IAccessTokenIssuer
{
	AccessToken Issue(Guid userId, string username, AuthScope scope, Guid? deviceId);
}
