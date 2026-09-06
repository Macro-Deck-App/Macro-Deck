using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Icons;

public sealed record ExtractedAppIcon(byte[] Content, string FileName);

public interface IAppIconExtractor
{
	bool CanExtract(string path);

	Task<Result<ExtractedAppIcon, IconError>> Extract(string path, CancellationToken cancellationToken);

	bool CanExtractContent(string fileName);

	Task<Result<ExtractedAppIcon, IconError>> ExtractFromContent(string fileName,
		Stream content,
		CancellationToken cancellationToken);
}
