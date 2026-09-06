using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public interface IFolderPortabilityService
{
	Result<string, PortabilityError> GetExportFileName(Guid folderId);

	Task<Result<byte[], PortabilityError>> Export(Guid folderId,
		PortableExportOptions options,
		CancellationToken cancellationToken);

	Task<Result<IReadOnlyList<FolderEntity>, PortabilityError>> Import(Guid profileId,
		Guid? parentFolderId,
		byte[] archiveBytes,
		string? password,
		CancellationToken cancellationToken);
}
