using System.Security.Cryptography;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeckHost.Application.Plugins.Installation;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

public sealed record PluginArtifactAcquisition
{
	public required bool Success { get; init; }

	public string? ArtifactPath { get; init; }

	public string? Sha256 { get; init; }

	public bool HostOwned { get; init; }

	public PluginArtifactSourceKind SourceKind { get; init; }

	public PluginInstallError? Error { get; init; }

	public string? ErrorMessage { get; init; }

	public static PluginArtifactAcquisition Ok(string artifactPath,
		string sha256,
		bool hostOwned,
		PluginArtifactSourceKind sourceKind)
	{
		return new PluginArtifactAcquisition
		{
			Success = true,
			ArtifactPath = artifactPath,
			Sha256 = sha256,
			HostOwned = hostOwned,
			SourceKind = sourceKind
		};
	}

	public static PluginArtifactAcquisition Fail(PluginInstallError error, string message)
	{
		return new PluginArtifactAcquisition { Success = false, Error = error, ErrorMessage = message };
	}
}

public interface IPluginArtifactAcquirer
{
	Task<PluginArtifactAcquisition> Acquire(PluginArtifactSource source,
		string stagingDirectory,
		CancellationToken cancellationToken = default);
}

public sealed class PluginArtifactAcquirer : IPluginArtifactAcquirer
{
	public const string HttpClientName = "plugin-artifact";

	private const string StagedFileName = "artifact" + PluginArtifactFiles.MacroDeckPluginExtension;
	private const int CopyBufferSize = 81_920;
	private const long ProgressByteInterval = 64 * 1024;
	private static readonly TimeSpan _progressTimeInterval = TimeSpan.FromMilliseconds(250);

	private readonly IHttpClientFactory _httpClientFactory;
	private readonly PluginInstallerOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public PluginArtifactAcquirer(IHttpClientFactory httpClientFactory,
		PluginInstallerOptions options,
		ILogger logger,
		TimeProvider? timeProvider = null)
	{
		_httpClientFactory = httpClientFactory;
		_options = options;
		_timeProvider = timeProvider ?? TimeProvider.System;
		_logger = logger.ForContext<PluginArtifactAcquirer>();
	}

	public async Task<PluginArtifactAcquisition> Acquire(PluginArtifactSource source,
		string stagingDirectory,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);

		return source.Kind switch
		{
			PluginArtifactSourceKind.LocalPath => await AcquireLocalPath(source,
				stagingDirectory,
				cancellationToken),
			PluginArtifactSourceKind.Upload => await AcquireUpload(source, stagingDirectory, cancellationToken),
			PluginArtifactSourceKind.Url => await AcquireUrl(source, stagingDirectory, cancellationToken),
			_ => PluginArtifactAcquisition.Fail(PluginInstallError.Failed,
				$"Unknown artifact source kind '{source.Kind}'.")
		};
	}

	// Copies into staging - like AcquireUpload - rather than handing back the caller's own path with
	// hostOwned: false. The caller's path is outside the host's control between here and promotion, so
	// reading it twice (once to verify, once to extract) would let it be swapped in between; copying it
	// once, under a hash computed from the copy's own bytes, closes that window.
	private async Task<PluginArtifactAcquisition> AcquireLocalPath(PluginArtifactSource source,
		string stagingDirectory,
		CancellationToken cancellationToken)
	{
		var path = source.Path;
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return PluginArtifactAcquisition.Fail(PluginInstallError.ArtifactNotFound,
				$"No artifact at '{path}'.");
		}

		if (!PluginArtifactFiles.HasArtifactExtension(path))
		{
			return PluginArtifactAcquisition.Fail(PluginInstallError.InvalidArchive,
				$"'{path}' does not have the {PluginArtifactFiles.MacroDeckPluginExtension} extension.");
		}

		var stagedPath = Path.Combine(stagingDirectory, StagedFileName);
		try
		{
			Directory.CreateDirectory(stagingDirectory);

			string sha256;
			await using (var stream = new FileStream(path,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read | FileShare.Delete))
			{
				var written = await CopyBounded(stream,
					stagedPath,
					PluginArtifactLimits.MaxArchiveBytes,
					progress: null,
					totalBytes: null,
					cancellationToken);
				if (written is not { } computed)
				{
					TryDeleteFile(stagedPath);
					return PluginArtifactAcquisition.Fail(PluginInstallError.ArtifactTooLarge,
						$"The artifact exceeds the {PluginArtifactLimits.MaxArchiveBytes}-byte limit.");
				}

				sha256 = computed;
			}

			return AcquireLocalPathResult(source, stagedPath, sha256);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			TryDeleteFile(stagedPath);
			PluginInstallInfrastructureLog.ArtifactStagingFailed(_logger, stagedPath, ex);
			return PluginArtifactAcquisition.Fail(PluginInstallError.StagingFailed,
				$"The artifact could not be staged: {ex.Message}");
		}
	}

	private static PluginArtifactAcquisition AcquireLocalPathResult(PluginArtifactSource source,
		string stagedPath,
		string sha256)
	{
		if (source.ExpectedSha256 is { } expected &&
			!string.Equals(expected, sha256, StringComparison.OrdinalIgnoreCase))
		{
			TryDeleteFile(stagedPath);
			return PluginArtifactAcquisition.Fail(PluginInstallError.HashMismatch,
				$"The artifact hashes to '{sha256}', not the expected '{expected}'.");
		}

		return PluginArtifactAcquisition.Ok(stagedPath, sha256, hostOwned: true, PluginArtifactSourceKind.LocalPath);
	}

	private async Task<PluginArtifactAcquisition> AcquireUpload(PluginArtifactSource source,
		string stagingDirectory,
		CancellationToken cancellationToken)
	{
		if (source.Content is null)
		{
			return PluginArtifactAcquisition.Fail(PluginInstallError.InvalidArchive,
				"The upload source declares no content stream.");
		}

		var stagedPath = Path.Combine(stagingDirectory, StagedFileName);
		try
		{
			Directory.CreateDirectory(stagingDirectory);
			var written = await CopyBounded(source.Content,
				stagedPath,
				PluginArtifactLimits.MaxArchiveBytes,
				progress: null,
				totalBytes: null,
				cancellationToken);
			if (written is not { } sha256)
			{
				TryDeleteFile(stagedPath);
				return PluginArtifactAcquisition.Fail(PluginInstallError.ArtifactTooLarge,
					$"The uploaded artifact exceeds the {PluginArtifactLimits.MaxArchiveBytes}-byte limit.");
			}

			if (source.ExpectedSha256 is { } expected &&
				!string.Equals(expected, sha256, StringComparison.OrdinalIgnoreCase))
			{
				TryDeleteFile(stagedPath);
				return PluginArtifactAcquisition.Fail(PluginInstallError.HashMismatch,
					$"The uploaded artifact hashes to '{sha256}', not the expected '{expected}'.");
			}

			return PluginArtifactAcquisition.Ok(stagedPath, sha256, hostOwned: true, PluginArtifactSourceKind.Upload);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			TryDeleteFile(stagedPath);
			PluginInstallInfrastructureLog.ArtifactStagingFailed(_logger, stagedPath, ex);
			return PluginArtifactAcquisition.Fail(PluginInstallError.StagingFailed,
				$"The uploaded artifact could not be staged: {ex.Message}");
		}
	}

	private async Task<PluginArtifactAcquisition> AcquireUrl(PluginArtifactSource source,
		string stagingDirectory,
		CancellationToken cancellationToken)
	{
		if (source.Url is null)
		{
			return PluginArtifactAcquisition.Fail(PluginInstallError.InvalidArchive,
				"The URL source declares no URL.");
		}

		if (!string.Equals(source.Url.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
		{
			return PluginArtifactAcquisition.Fail(PluginInstallError.InvalidArchive,
				$"'{source.Url}' is not an https URL; only https downloads are allowed.");
		}

		var stagedPath = Path.Combine(stagingDirectory, StagedFileName);
		try
		{
			Directory.CreateDirectory(stagingDirectory);

			using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			linkedCts.CancelAfter(_options.DownloadTimeout);

			var client = _httpClientFactory.CreateClient(HttpClientName);
			using var response = await client.GetAsync(source.Url,
				HttpCompletionOption.ResponseHeadersRead,
				linkedCts.Token);
			response.EnsureSuccessStatusCode();

			await using var responseStream = await response.Content.ReadAsStreamAsync(linkedCts.Token);
			var written = await CopyBounded(responseStream,
				stagedPath,
				PluginArtifactLimits.MaxArchiveBytes,
				source.Progress,
				response.Content.Headers.ContentLength,
				linkedCts.Token);
			if (written is not { } sha256)
			{
				TryDeleteFile(stagedPath);
				return PluginArtifactAcquisition.Fail(PluginInstallError.ArtifactTooLarge,
					$"The downloaded artifact exceeds the {PluginArtifactLimits.MaxArchiveBytes}-byte limit.");
			}

			if (source.ExpectedSha256 is { } expected &&
				!string.Equals(expected, sha256, StringComparison.OrdinalIgnoreCase))
			{
				TryDeleteFile(stagedPath);
				return PluginArtifactAcquisition.Fail(PluginInstallError.HashMismatch,
					$"The downloaded artifact hashes to '{sha256}', not the expected '{expected}'.");
			}

			return PluginArtifactAcquisition.Ok(stagedPath, sha256, hostOwned: true, PluginArtifactSourceKind.Url);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			TryDeleteFile(stagedPath);
			throw;
		}
		catch (Exception ex) when (ex is HttpRequestException
			or IOException
			or UnauthorizedAccessException
			or OperationCanceledException
			or TaskCanceledException)
		{
			TryDeleteFile(stagedPath);
			PluginInstallInfrastructureLog.ArtifactDownloadFailed(_logger, source.Url.ToString(), ex);
			return PluginArtifactAcquisition.Fail(PluginInstallError.Failed,
				$"The plugin artifact could not be downloaded from '{source.Url}'.");
		}
	}

	private async Task<string?> CopyBounded(Stream source,
		string destinationPath,
		long limit,
		IProgress<PluginArtifactDownloadProgress>? progress,
		long? totalBytes,
		CancellationToken cancellationToken)
	{
		await using var destination
			= new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
		using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
		var chunk = new byte[CopyBufferSize];
		var total = 0L;
		var lastReportedBytes = 0L;
		var lastReportedAt = _timeProvider.GetTimestamp();
		int read;
		while ((read = await source.ReadAsync(chunk, cancellationToken)) > 0)
		{
			total += read;
			if (total > limit)
			{
				return null;
			}

			incrementalHash.AppendData(chunk, 0, read);
			await destination.WriteAsync(chunk.AsMemory(0, read), cancellationToken);

			if (progress is not null &&
				(total - lastReportedBytes >= ProgressByteInterval ||
					_timeProvider.GetElapsedTime(lastReportedAt) >= _progressTimeInterval))
			{
				progress.Report(new PluginArtifactDownloadProgress(total, totalBytes));
				lastReportedBytes = total;
				lastReportedAt = _timeProvider.GetTimestamp();
			}
		}

		progress?.Report(new PluginArtifactDownloadProgress(total, totalBytes));

		return AssetContentHash.Sha256Prefix + Convert.ToHexStringLower(incrementalHash.GetHashAndReset());
	}

	private static void TryDeleteFile(string path)
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
