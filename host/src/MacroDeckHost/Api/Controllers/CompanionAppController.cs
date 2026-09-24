using MacroDeckHost.Application.Ui.Transport.Messages.CompanionApp;
using MacroDeckHost.CompanionApp;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/settings/companion-app")]
public class CompanionAppController : ControllerBase
{
	private readonly CompanionAppService _companionApp;

	public CompanionAppController(CompanionAppService companionApp)
	{
		_companionApp = companionApp;
	}

	[HttpGet]
	public Task<CompanionAppStatus> Get(CancellationToken ct) => _companionApp.GetStatusAsync(ct);

	[HttpPut]
	public Task<CompanionAppStatus> Update([FromBody] UpdateCompanionAppSettingsRequest request, CancellationToken ct)
		=> _companionApp.SetAutoUpdateAsync(request.AutoUpdate, ct);

	[HttpPost("check")]
	public Task<CompanionAppStatus> Check(CancellationToken ct) => _companionApp.CheckNowAsync(ct);

	[HttpGet("apk")]
	public async Task<IActionResult> DownloadApk(CancellationToken ct)
	{
		var (path, release, error) = await _companionApp.PrepareApkAsync(ct);
		if (path is null || release is null)
		{
			return StatusCode(StatusCodes.Status502BadGateway, new { error });
		}

		return PhysicalFile(path, "application/vnd.android.package-archive", $"macro-deck-companion-{release.Version}.apk");
	}

	[HttpPost("install")]
	public async Task<ActionResult<InstallCompanionAppResponse>> Install([FromBody] InstallCompanionAppRequest request,
		CancellationToken ct)
	{
		if (string.IsNullOrWhiteSpace(request.Serial))
		{
			return BadRequest();
		}

		var (error, status) = await _companionApp.InstallAsync(request.Serial, ct);
		return new InstallCompanionAppResponse { Success = error is null, Error = error, Status = status };
	}
}
