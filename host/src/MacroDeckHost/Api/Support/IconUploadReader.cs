using System.Runtime.CompilerServices;
using MacroDeckHost.Application.Icons;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace MacroDeckHost.Api.Support;

public static class IconUploadReader
{
	public const long MaxRequestBytes = 1_073_741_824;

	public static IAsyncEnumerable<IconImportFile>? TryReadFiles(HttpRequest request,
		CancellationToken cancellationToken)
	{
		if (!MediaTypeHeaderValue.TryParse(request.ContentType, out var contentType) ||
			!contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
		if (string.IsNullOrEmpty(boundary))
		{
			return null;
		}

		return ReadFiles(new MultipartReader(boundary, request.Body), cancellationToken);
	}

	private static async IAsyncEnumerable<IconImportFile> ReadFiles(MultipartReader reader,
		[EnumeratorCancellation] CancellationToken cancellationToken)
	{
		while (await reader.ReadNextSectionAsync(cancellationToken) is { } section)
		{
			if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
			{
				continue;
			}

			var fileName = HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value ??
				HeaderUtilities.RemoveQuotes(disposition.FileName).Value;
			if (string.IsNullOrEmpty(fileName))
			{
				continue;
			}

			yield return new IconImportFile(fileName, section.Body);
		}
	}
}
