using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public interface IProfilePortabilityService
{
	Result<string, PortabilityError> GetExportFileName(Guid profileId);

	Task<Result<byte[], PortabilityError>> Export(Guid profileId,
		PortableExportOptions options,
		CancellationToken cancellationToken);

	Task<Result<ProfileEntity, PortabilityError>> Import(byte[] archiveBytes,
		string? password,
		CancellationToken cancellationToken);

	/// <summary>
	/// Imports content that did not come out of an archive - a migration from another application builds
	/// the very same model. Everything downstream of reading the file is shared: fresh ids for the profile,
	/// its folders and its widgets, reference rewriting inside widget data, persistence and the created
	/// notifications.
	/// </summary>
	/// <remarks>
	/// A caller that already resolved its images to local icon ids passes no <paramref name="iconFiles" />;
	/// those ids are left alone, because only the ids minted here are ever rewritten.
	/// </remarks>
	Task<Result<ProfileEntity, PortabilityError>> ImportContent(PortableContent content,
		IReadOnlyList<PortableIconFile> iconFiles,
		CancellationToken cancellationToken);
}
