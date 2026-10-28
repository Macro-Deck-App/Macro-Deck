using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/secret-approvals")]
[Authorize(Policy = AuthPolicies.Admin)]
public class SecretApprovalsController : ControllerBase
{
	private readonly SecretApprovalCatalog _catalog;

	public SecretApprovalsController(SecretApprovalCatalog catalog)
	{
		_catalog = catalog;
	}

	[HttpGet]
	public PendingSecretApprovalsResponse GetPending()
	{
		return _catalog.Pending();
	}

	[HttpPost("approve")]
	public ApproveSecretBlockResponse Approve(ApproveSecretBlockRequest body)
	{
		ArgumentNullException.ThrowIfNull(body);

		return _catalog.Approve(body);
	}
}
