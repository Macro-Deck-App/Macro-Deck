using System.Security.Claims;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Application.Secrets.Approvals;

// Scoped per HTTP request and per WebSocket connection, never ambient: work that creates its own
// scope (pumps, timers, reconcilers) therefore writes with Preserve.
public sealed class SecretWriteAuthority
{
	public SecretApprovalMode Mode { get; private set; } = SecretApprovalMode.Preserve;

	public SecretApprovalWrite Write => new(Mode);

	public void Grant(ClaimsPrincipal? principal)
	{
		Mode = principal?.HasClaim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope) == true
			? SecretApprovalMode.Approve
			: SecretApprovalMode.Preserve;
	}
}
