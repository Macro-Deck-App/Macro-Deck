using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Icons;

internal sealed class FakeAppIconExtractor : IAppIconExtractor
{
	private static readonly HashSet<string> _containerExtensions = new(StringComparer.OrdinalIgnoreCase)
	{
		".ico", ".icns", ".exe", ".dll"
	};

	public List<string> Requested { get; } = [];

	public List<string> RequestedContent { get; } = [];

	public Dictionary<string, Result<ExtractedAppIcon, IconError>> Results { get; } = new();

	public Result<ExtractedAppIcon, IconError> Result { get; set; } =
		Domain.Common.Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat);

	public bool CanExtract(string path) => IconImportFiles.IsIconDropSource(path);

	public Task<Result<ExtractedAppIcon, IconError>> Extract(string path, CancellationToken cancellationToken)
	{
		Requested.Add(path);
		return Task.FromResult(Results.TryGetValue(path, out var result) ? result : Result);
	}

	public bool CanExtractContent(string fileName)
		=> _containerExtensions.Contains(Path.GetExtension(fileName));

	// Forces ExtractFromContent to read the incoming stream to the end and echo those bytes back,
	// rather than a canned Results entry - used by the test that guards against the caller accidentally
	// touching Length/Position/Seek on a forward-only multipart section.
	public bool EchoContent { get; set; }

	public async Task<Result<ExtractedAppIcon, IconError>> ExtractFromContent(string fileName,
		Stream content,
		CancellationToken cancellationToken)
	{
		RequestedContent.Add(fileName);
		if (EchoContent)
		{
			using var buffer = new MemoryStream();
			var chunk = new byte[4096];
			int read;
			while ((read = await content.ReadAsync(chunk, cancellationToken)) > 0)
			{
				await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
			}

			var name = Path.GetFileNameWithoutExtension(fileName);
			return Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon(buffer.ToArray(),
				name + ".png"));
		}

		return Results.TryGetValue(fileName, out var result) ? result : Result;
	}
}
