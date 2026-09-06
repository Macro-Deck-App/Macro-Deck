using MacroDeckHost.Application.ClientTargets;

namespace MacroDeckHost.Infrastructure.ClientTargets;

internal sealed class FileSystemScratchWriter : IFileSystemScratchWriter
{
	public async Task<string> WriteAsync(string content, CancellationToken cancellationToken)
	{
		var path = Path.Combine(Path.GetTempPath(), $"macro-deck-{Guid.NewGuid():N}.tmp");
		await File.WriteAllTextAsync(path, content, cancellationToken);
		return path;
	}

	public void Delete(string path)
	{
		try
		{
			File.Delete(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			// A leftover temp file is not worth failing a completed provisioning run over.
		}
	}
}
