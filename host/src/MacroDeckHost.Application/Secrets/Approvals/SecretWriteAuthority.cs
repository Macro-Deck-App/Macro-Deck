using System.Security.Claims;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Application.Secrets.Approvals;

// Scoped per HTTP request and per WebSocket connection, never ambient: work that creates its own
// scope (pumps, timers, reconcilers) therefore writes with Preserve.
public sealed class SecretWriteAuthority
{
	private SecretApprovalWrite _write = SecretApprovalWrite.Preserve;

	public SecretApprovalMode Mode => _write.Mode;

	public SecretApprovalWrite Write => _write;

	public void Grant(ClaimsPrincipal? principal)
	{
		_write = principal?.HasClaim(AuthDefaults.ScopeClaim, AuthDefaults.AdminScope) == true
			? SecretApprovalWrite.Approve
			: principal?.HasClaim(AuthDefaults.ScopeClaim, AuthDefaults.McpScope) == true
				? SecretApprovalWrite.Withhold
				: SecretApprovalWrite.Preserve;
	}

	// The MCP server writes as an untrusted principal; its undo journal alone restores recorded approvals.
	public void Use(SecretApprovalWrite write)
	{
		_write = write;
	}
}
