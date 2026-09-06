using System.Security.Cryptography;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Store;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreArtifactDownloader : IStoreArtifactDownloader
{
	private const string ArtifactFileName = "artifact.bin";
	private const int CopyBufferSize = 81_920;
	private const long ProgressByteInterval = 64 * 1024;
	private static readonly TimeSpan _progressTimeInterval = TimeSpan.FromMilliseconds(250);

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly StoreRegistryOptions _options;
	private readonly IMacroDeckPaths _paths;
	private readonly TimeProvider _timeProvider;

	public StoreArtifactDownloader(IHttpClientFactory httpClientFactory,
		StoreRegistryOptions options,
		IMacroDeckPaths paths,
		TimeProvider timeProvider)
	{
		_httpClientFactory = httpClientFactory;
		_options = options;
		_paths = paths;
		_timeProvider = timeProvider;
	}

	public async Task<StoreArtifactDownloadResult> Download(Uri artifactUrl,
		string expectedSha256Hex,
		long expectedSize,
		Guid operationId,
		IProgress<StoreArtifactDownloadProgress>? progress,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(artifactUrl);
		ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256Hex);

		if (!StoreHttp.IsHttps(artifactUrl))
		{
			return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.DownloadFailed,
				$"'{artifactUrl}' is not an https URL.");
		}

		if (expectedSize > _options.MaxArtifactBytes)
		{
			return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.ArtifactTooLarge,
				$"'{artifactUrl}' declares {expectedSize} bytes, above the {_options.MaxArtifactBytes} byte " +
				"limit.");
		}

		var directory = Path.Combine(_paths.StoreStagingDirectory, operationId.ToString("N"));
		Directory.CreateDirectory(directory);
		var destination = Path.Combine(directory, ArtifactFileName);

		HttpResponseMessage response;
		try
		{
			var client = _httpClientFactory.CreateClient(StoreHttp.ArtifactClientName);
			client.Timeout = _options.DownloadTimeout;
			response = await client.GetAsync(artifactUrl,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
		{
			return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.DownloadFailed,
				$"'{artifactUrl}' could not be fetched: {ex.Message}");
		}

		using (response)
		{
			if (!response.IsSuccessStatusCode)
			{
				return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.DownloadFailed,
					$"'{artifactUrl}' answered {(int)response.StatusCode}.");
			}

			return await CopyBounded(response,
				artifactUrl,
				expectedSha256Hex,
				expectedSize,
				destination,
				progress,
				cancellationToken);
		}
	}

	private async Task<StoreArtifactDownloadResult> CopyBounded(HttpResponseMessage response,
		Uri artifactUrl,
		string expectedSha256Hex,
		long expectedSize,
		string destination,
		IProgress<StoreArtifactDownloadProgress>? progress,
		CancellationToken cancellationToken)
	{
		var limit = Math.Min(expectedSize, _options.MaxArtifactBytes);
		var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None);
		try
		{
			using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);

			var chunk = new byte[CopyBufferSize];
			var total = 0L;
			var lastReportedBytes = 0L;
			var lastReportedAt = _timeProvider.GetTimestamp();
			int read;
			while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
			{
				total += read;
				if (total > limit)
				{
					await fileStream.DisposeAsync();
					TryDelete(destination);
					return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.ArtifactTooLarge,
						$"'{artifactUrl}' exceeded {limit} bytes.");
				}

				hash.AppendData(chunk, 0, read);
				await fileStream.WriteAsync(chunk.AsMemory(0, read), cancellationToken);

				if (progress is not null &&
					(total - lastReportedBytes >= ProgressByteInterval ||
						_timeProvider.GetElapsedTime(lastReportedAt) >= _progressTimeInterval))
				{
					progress.Report(new StoreArtifactDownloadProgress(total, expectedSize));
					lastReportedBytes = total;
					lastReportedAt = _timeProvider.GetTimestamp();
				}
			}

			await fileStream.DisposeAsync();
			progress?.Report(new StoreArtifactDownloadProgress(total, expectedSize));

			if (total != expectedSize)
			{
				TryDelete(destination);
				return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.SizeMismatch,
					$"'{artifactUrl}' returned {total} bytes, not the declared {expectedSize}.");
			}

			var digest = Convert.ToHexStringLower(hash.GetHashAndReset());
			if (!string.Equals(digest, expectedSha256Hex, StringComparison.OrdinalIgnoreCase))
			{
				TryDelete(destination);
				return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.ChecksumMismatch,
					$"'{artifactUrl}' hashes to '{digest}', not the expected '{expectedSha256Hex}'.");
			}

			return StoreArtifactDownloadResult.Ok(destination, total);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			await fileStream.DisposeAsync();
			TryDelete(destination);
			return StoreArtifactDownloadResult.Fail(StoreArtifactDownloadError.DownloadFailed,
				$"The artifact could not be staged: {ex.Message}");
		}
		finally
		{
			await fileStream.DisposeAsync();
		}
	}

	private static void TryDelete(string path)
	{
		try
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
		}
	}
}
