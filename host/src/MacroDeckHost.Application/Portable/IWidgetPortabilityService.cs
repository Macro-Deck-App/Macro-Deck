using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Portable;

public interface IWidgetPortabilityService
{
	Task<Result<byte[], PortabilityError>> Export(Guid folderId,
		IReadOnlyList<Guid> widgetIds,
		PortableExportOptions options,
		CancellationToken cancellationToken);

	Task<Result<IReadOnlyList<WidgetEntity>, PortabilityError>> Import(Guid folderId,
		int anchorX,
		int anchorY,
		byte[] archiveBytes,
		string? password,
		CancellationToken cancellationToken);
}
