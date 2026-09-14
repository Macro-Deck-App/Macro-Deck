using MacroDeckHost.Application.Licensing;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/settings/license")]
public class CompanionLicenseController : ControllerBase
{
	private readonly ICompanionLicenseService _licenses;

	public CompanionLicenseController(ICompanionLicenseService licenses)
	{
		_licenses = licenses;
	}

	[HttpGet]
	public Task<CompanionLicenseStatus> Get(CancellationToken ct) => _licenses.GetStatusAsync(ct);

	[HttpPost("test")]
	public async Task<ActionResult<CompanionLicenseStatus>> IssueTest(CancellationToken ct)
	{
		if (await _licenses.IssueTestLicenseAsync(ct) is { } status)
		{
			return status;
		}

		return NotFound();
	}

	[HttpDelete("test")]
	public Task<CompanionLicenseStatus> RevokeTest(CancellationToken ct) => _licenses.RevokeTestLicenseAsync(ct);
}
