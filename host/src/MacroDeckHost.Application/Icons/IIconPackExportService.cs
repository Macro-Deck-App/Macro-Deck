using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public interface IIconPackExportService
{
	Result<string, IconPackError> GetExportFileName(Guid packId);

	Task<Result<IconPackError>> Export(Guid packId, Stream destination, CancellationToken cancellationToken);
}
