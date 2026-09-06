namespace MacroDeckHost.Application.Store;

public readonly record struct StoreArtifactDownloadProgress(long BytesRead, long? TotalBytes);

public enum StoreArtifactDownloadError
{
	DownloadFailed,
	SizeMismatch,
	ChecksumMismatch,
	ArtifactTooLarge
}

public sealed record StoreArtifactDownloadResult
{
	public required bool Success { get; init; }

	public string? FilePath { get; init; }

	public long Size { get; init; }

	public StoreArtifactDownloadError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public static StoreArtifactDownloadResult Ok(string filePath, long size) =>
		new() { Success = true, FilePath = filePath, Size = size };

	public static StoreArtifactDownloadResult Fail(StoreArtifactDownloadError error, string message) =>
		new() { Success = false, Error = error, ErrorMessage = message };
}

/// <summary>Downloads a store release artifact to a staging file, https-only, capped at
/// <see cref="StoreRegistryOptions.MaxArtifactBytes" />, hashing incrementally while writing. The
/// downloaded byte count and digest are both checked against the caller-supplied release manifest values
/// before the result is reported successful.</summary>
public interface IStoreArtifactDownloader
{
	Task<StoreArtifactDownloadResult> Download(Uri artifactUrl,
		string expectedSha256Hex,
		long expectedSize,
		Guid operationId,
		IProgress<StoreArtifactDownloadProgress>? progress,
		CancellationToken cancellationToken = default);
}
