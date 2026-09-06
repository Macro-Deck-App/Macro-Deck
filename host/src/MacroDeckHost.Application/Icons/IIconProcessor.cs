using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public interface IIconProcessor
{
	Task<Result<ProcessedIconResult, IconError>> Process(Stream original,
		string originalFileName,
		CancellationToken cancellationToken);
}
