using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Portable;
using MacroDeckHost.Application.Ui.Transport.Messages.Profiles;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
	private readonly IUiTransportMessageHandler<GetProfilesRequest, GetProfilesResponse> _getProfiles;
	private readonly IUiTransportMessageHandler<CreateProfileRequest, CreateProfileResponse> _createProfile;
	private readonly IUiTransportMessageHandler<UpdateProfileRequest, UpdateProfileResponse> _updateProfile;
	private readonly IUiTransportMessageHandler<DeleteProfileRequest, DeleteProfileResponse> _deleteProfile;
	private readonly IProfilePortabilityService _portability;

	public ProfilesController(
		IUiTransportMessageHandler<GetProfilesRequest, GetProfilesResponse> getProfiles,
		IUiTransportMessageHandler<CreateProfileRequest, CreateProfileResponse> createProfile,
		IUiTransportMessageHandler<UpdateProfileRequest, UpdateProfileResponse> updateProfile,
		IUiTransportMessageHandler<DeleteProfileRequest, DeleteProfileResponse> deleteProfile,
		IProfilePortabilityService portability)
	{
		_getProfiles = getProfiles;
		_createProfile = createProfile;
		_updateProfile = updateProfile;
		_deleteProfile = deleteProfile;
		_portability = portability;
	}

	[HttpGet]
	[Authorize(Policy = AuthPolicies.ClientAccess)]
	public Task<GetProfilesResponse> GetAll(CancellationToken ct)
		=> _getProfiles.Handle(new GetProfilesRequest(), ct).AsTask();

	[HttpPost]
	public Task<CreateProfileResponse> Create(CreateProfileRequest body, CancellationToken ct)
		=> _createProfile.Handle(body, ct).AsTask();

	[HttpPut]
	public Task<UpdateProfileResponse> Update(UpdateProfileRequest body, CancellationToken ct)
		=> _updateProfile.Handle(body, ct).AsTask();

	[HttpDelete("{id}")]
	public Task<DeleteProfileResponse> Delete(string id, CancellationToken ct)
		=> _deleteProfile.Handle(new DeleteProfileRequest { Id = id }, ct).AsTask();

	[HttpPost("{id}/export")]
	public async Task<IActionResult> Export(string id, ExportProfileRequest body, CancellationToken ct)
	{
		if (!Guid.TryParse(id, out var profileId))
		{
			return NotFound();
		}

		var fileName = _portability.GetExportFileName(profileId);
		if (!fileName.Success)
		{
			return PortabilityHttp.ToErrorResult(this, fileName.Error!.Value, fileName.ErrorMessage);
		}

		var result = await _portability.Export(profileId, body.ToOptions(), ct);
		if (!result.Success)
		{
			return PortabilityHttp.ToErrorResult(this, result.Error!.Value, result.ErrorMessage);
		}

		return File(result.Data!, "application/zip", fileName.Data);
	}

	[HttpPost("import")]
	[RequestSizeLimit(PortabilityHttp.MaxRequestBytes)]
	public async Task<ImportProfileResponse> Import([FromForm] IFormFile? file,
		[FromForm] string? password,
		CancellationToken ct)
	{
		var bytes = await ArchivePathReader.ReadUpload(file, ct);
		if (!bytes.Success)
		{
			return Fail(bytes.Error!.Value, bytes.ErrorMessage);
		}

		return await Import(bytes.Data!, password, ct);
	}

	[HttpPost("import-path")]
	public async Task<ImportProfileResponse> ImportFromPath(ImportProfileFromPathRequest body, CancellationToken ct)
	{
		var refusal = PortabilityHttp.RefuseUnlessDesktop(HttpContext);
		if (refusal is not null)
		{
			return new ImportProfileResponse { Success = false, Error = refusal };
		}

		var bytes = await ArchivePathReader.Read(body.Path,
			[PortableFileExtensions.Profile],
			PortabilityHttp.MaxRequestBytes,
			ct);
		if (!bytes.Success)
		{
			return Fail(bytes.Error!.Value, bytes.ErrorMessage);
		}

		return await Import(bytes.Data!, body.Password, ct);
	}

	private async Task<ImportProfileResponse> Import(byte[] archiveBytes, string? password, CancellationToken ct)
	{
		var result = await _portability.Import(archiveBytes, password, ct);
		if (!result.Success)
		{
			return Fail(result.Error!.Value, result.ErrorMessage);
		}

		return new ImportProfileResponse
		{
			Success = true,
			Profile = ProfileDtoMapper.MapJsonProfile(result.Data!)
		};
	}

	private static ImportProfileResponse Fail(Domain.Enums.PortabilityError error, string? message)
		=> new()
		{
			Success = false,
			Error = PortabilityHttp.ToTransportError(error, message)
		};
}
