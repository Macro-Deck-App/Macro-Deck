namespace MacroDeckHost.Application.ClientTargets;

/// <summary>
/// Writes a short-lived local file for a transfer that needs a real path on disk - <c>adb push</c>
/// takes a file, not a stream. Behind an interface so provisioning is testable without touching the
/// filesystem.
/// </summary>
public interface IFileSystemScratchWriter
{
	Task<string> WriteAsync(string content, CancellationToken cancellationToken);

	/// <summary>Removes a file written by <see cref="WriteAsync"/>. Never throws.</summary>
	void Delete(string path);
}
