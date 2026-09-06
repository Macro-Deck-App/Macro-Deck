using System.Text.Json;
using MacroDeck.Signing;
using MacroDeck.Signing.Registry;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Domain.Common;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreRegistryRefresher : IStoreRegistryRefresher, IDisposable
{
	private const string ManifestFileName = "registry-manifest.json";
	private const string SignatureFileName = "registry-signature.json";
	private const string CertificateDirectory = "certificates";

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly StoreRegistryOptions _options;
	private readonly IStoreRegistryStateStore _stateStore;
	private readonly StoreRegistryReader _reader;
	private readonly IStoreCatalog _catalog;
	private readonly IMacroDeckPaths _paths;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private StoreRegistryStatus _status = StoreRegistryStatus.Unavailable;

	public StoreRegistryRefresher(IHttpClientFactory httpClientFactory,
		StoreRegistryOptions options,
		IStoreRegistryStateStore stateStore,
		StoreRegistryReader reader,
		IStoreCatalog catalog,
		IMacroDeckPaths paths,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_httpClientFactory = httpClientFactory;
		_options = options;
		_stateStore = stateStore;
		_reader = reader;
		_catalog = catalog;
		_paths = paths;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StoreRegistryRefresher>();
	}

	public StoreRegistryStatus Status => _status with { Stale = IsStale(_status) };

	private string Origin => _options.BaseUrl.GetLeftPart(UriPartial.Path);

	public async Task LoadCachedRegistry(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var current = _paths.StoreRegistryCurrentDirectory;
			if (!File.Exists(Path.Combine(current, ManifestFileName)))
			{
				return;
			}

			var state = _stateStore.Load(Origin);
			var verified = await VerifyTree(current, state?.AcceptedSequence ?? 0, cancellationToken);
			if (!verified.Success)
			{
				// The cache lives in a user-writable directory, so a tree that no longer verifies is
				// discarded outright rather than served: the store simply reports unavailable until a
				// refresh succeeds.
				_logger.Warning("Discarding the cached store registry: {Reason}.", verified.FailureMessage);
				SafeDelete(current);
				_status = _status with { LastError = verified.Error, LastErrorMessage = verified.FailureMessage };
				return;
			}

			var snapshot = _reader.Read(current,
				verified.Sequence,
				verified.GeneratedAt,
				verified.SignedAt,
				state?.LastSuccessAt ?? _timeProvider.GetUtcNow());
			if (snapshot is null)
			{
				SafeDelete(current);
				return;
			}

			_catalog.Swap(snapshot);
			_status = new StoreRegistryStatus
			{
				HasCatalog = true,
				Sequence = snapshot.Sequence,
				SignedAt = snapshot.SignedAt,
				FetchedAt = snapshot.FetchedAt,
				LastSuccessAt = state?.LastSuccessAt,
				CertificateId = verified.CertificateId
			};
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<Result<RegistryRefreshError>> Refresh(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		_status = _status with { Refreshing = true };
		var staging = Path.Combine(_paths.StoreRegistryStagingDirectory, Guid.CreateVersion7().ToString("N"));
		try
		{
			var result = await RefreshCore(staging, cancellationToken);
			_status = _status with
			{
				Refreshing = false,
				LastAttemptAt = _timeProvider.GetUtcNow(),
				LastError = result.Success ? null : result.Error,
				LastErrorMessage = result.Success ? null : result.ErrorMessage
			};

			return result;
		}
		finally
		{
			SafeDelete(staging);
			_gate.Release();
		}
	}

	private async Task<Result<RegistryRefreshError>> RefreshCore(string staging,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(staging);
		using var client = CreateClient(StoreHttp.RegistryClientName, _options.RequestTimeout);

		var manifestFetch = await Fetch(client,
			ManifestFileName,
			_options.MaxRegistryFileBytes,
			null,
			cancellationToken);
		if (!manifestFetch.Success)
		{
			return Result.Fail(RegistryRefreshError.NetworkFailure, manifestFetch.FailureMessage);
		}

		var signatureFetch = await Fetch(client,
			SignatureFileName,
			_options.MaxRegistryFileBytes,
			null,
			cancellationToken);
		if (!signatureFetch.Success)
		{
			return Result.Fail(RegistryRefreshError.NetworkFailure, signatureFetch.FailureMessage);
		}

		RegistryManifestDocument? manifest;
		RegistrySignatureFile? signature;
		try
		{
			manifest = JsonSerializer.Deserialize<RegistryManifestDocument>(manifestFetch.Content!,
				StoreRegistryJson.Options);
			signature = JsonSerializer.Deserialize<RegistrySignatureFile>(signatureFetch.Content!,
				StoreRegistryJson.Options);
		}
		catch (JsonException ex)
		{
			return Result.Fail(RegistryRefreshError.Malformed, ex.Message);
		}

		if (manifest is null ||
			signature is null ||
			manifest.Files.Count == 0 ||
			string.IsNullOrWhiteSpace(signature.KeyId))
		{
			return Result.Fail(RegistryRefreshError.Malformed, "The registry manifest or signature is unusable.");
		}

		var state = _stateStore.Load(Origin);
		var accepted = state?.AcceptedSequence ?? 0;
		if (manifest.Sequence < accepted)
		{
			return Result.Fail(RegistryRefreshError.SequenceRollback,
				$"The registry offered sequence {manifest.Sequence}, below the accepted {accepted}.");
		}

		if (manifest.Sequence == accepted &&
			string.Equals(state?.ManifestSha256, manifestFetch.Sha256, StringComparison.OrdinalIgnoreCase) &&
			_status.HasCatalog)
		{
			_stateStore.Save(state! with { LastSuccessAt = _timeProvider.GetUtcNow() });
			_status = _status with { LastSuccessAt = _timeProvider.GetUtcNow() };
			return Result.Ok<RegistryRefreshError>();
		}

		if (manifest.Files.Count > _options.MaxRegistryFiles ||
			manifest.Files.Sum(file => file.Size) > _options.MaxRegistryTotalBytes)
		{
			return Result.Fail(RegistryRefreshError.BudgetExceeded,
				"The registry manifest declares more data than this host will download.");
		}

		if (await DownloadDeclaredFiles(client, manifest, staging, cancellationToken) is { } downloadFailure)
		{
			return downloadFailure;
		}

		await File.WriteAllBytesAsync(Path.Combine(staging, ManifestFileName),
			manifestFetch.Content!,
			cancellationToken);
		await File.WriteAllBytesAsync(Path.Combine(staging, SignatureFileName),
			signatureFetch.Content!,
			cancellationToken);

		var verified = await VerifyTree(staging, accepted, cancellationToken);
		if (!verified.Success)
		{
			return Result.Fail(verified.Error!.Value, verified.FailureMessage);
		}

		if (RevokedSigningKey(staging, signature.KeyId) is { } revokedReason)
		{
			return Result.Fail(RegistryRefreshError.SigningKeyRevoked, revokedReason);
		}

		var fetchedAt = _timeProvider.GetUtcNow();
		var snapshot = _reader.Read(staging,
			manifest.Sequence,
			manifest.GeneratedAt,
			signature.SignedAt,
			fetchedAt);
		if (snapshot is null)
		{
			return Result.Fail(RegistryRefreshError.Malformed,
				"The registry index references packages the signed snapshot does not describe.");
		}

		if (!Promote(staging, out var promoteFailure))
		{
			return Result.Fail(RegistryRefreshError.StorageFailure, promoteFailure);
		}

		_stateStore.Save(new StoreRegistryState
		{
			Origin = Origin,
			AcceptedSequence = manifest.Sequence,
			ManifestSha256 = manifestFetch.Sha256,
			CertificateId = verified.CertificateId,
			SignedAt = signature.SignedAt,
			LastSuccessAt = fetchedAt
		});

		_catalog.Swap(snapshot);
		_status = new StoreRegistryStatus
		{
			HasCatalog = true,
			Sequence = snapshot.Sequence,
			SignedAt = snapshot.SignedAt,
			FetchedAt = fetchedAt,
			LastSuccessAt = fetchedAt,
			LastAttemptAt = fetchedAt,
			CertificateId = verified.CertificateId
		};

		return Result.Ok<RegistryRefreshError>();
	}

	private async Task<Result<RegistryRefreshError>?> DownloadDeclaredFiles(HttpClient client,
		RegistryManifestDocument manifest,
		string staging,
		CancellationToken cancellationToken)
	{
		foreach (var file in manifest.Files)
		{
			if (!IsSafeRelativePath(file.Path))
			{
				return Result.Fail(RegistryRefreshError.Malformed,
					$"The registry manifest declares an unsafe path '{file.Path}'.");
			}

			if (file.Size > _options.MaxRegistryFileBytes)
			{
				return Result.Fail(RegistryRefreshError.BudgetExceeded,
					$"'{file.Path}' declares {file.Size} bytes, above the per-file limit.");
			}

			var fetch = await Fetch(client,
				file.Path,
				_options.MaxRegistryFileBytes,
				file.Size,
				cancellationToken);
			if (!fetch.Success)
			{
				return Result.Fail(fetch.SizeMismatch
						? RegistryRefreshError.SizeMismatch
						: RegistryRefreshError.NetworkFailure,
					fetch.FailureMessage);
			}

			var destination = Path.Combine(staging, Path.Combine(file.Path.Split('/')));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			await File.WriteAllBytesAsync(destination, fetch.Content!, cancellationToken);
		}

		return null;
	}

	private async Task<VerifiedTree> VerifyTree(string root,
		long acceptedSequence,
		CancellationToken cancellationToken)
	{
		var manifestPath = Path.Combine(root, ManifestFileName);
		var signaturePath = Path.Combine(root, SignatureFileName);
		if (!File.Exists(manifestPath) || !File.Exists(signaturePath))
		{
			return VerifiedTree.Fail(RegistryRefreshError.Malformed, "The registry snapshot is incomplete.");
		}

		RegistryManifestDocument? manifest;
		RegistrySignatureFile? signature;
		try
		{
			manifest = JsonSerializer.Deserialize<RegistryManifestDocument>(
				await File.ReadAllBytesAsync(manifestPath, cancellationToken),
				StoreRegistryJson.Options);
			signature = JsonSerializer.Deserialize<RegistrySignatureFile>(
				await File.ReadAllBytesAsync(signaturePath, cancellationToken),
				StoreRegistryJson.Options);
		}
		catch (Exception ex) when (ex is JsonException or IOException)
		{
			return VerifiedTree.Fail(RegistryRefreshError.Malformed, ex.Message);
		}

		if (manifest is null || signature is null || string.IsNullOrWhiteSpace(signature.KeyId))
		{
			return VerifiedTree.Fail(RegistryRefreshError.Malformed, "The registry snapshot is unusable.");
		}

		// Signature and digest checks alone would still accept an older, validly signed snapshot dropped
		// into the cache directory, so the anti-rollback bound is enforced here too, not only on fetch.
		if (manifest.Sequence < acceptedSequence)
		{
			return VerifiedTree.Fail(RegistryRefreshError.SequenceRollback,
				$"The snapshot is at sequence {manifest.Sequence}, below the accepted {acceptedSequence}.");
		}

		var certificatePath = Path.Combine(root, CertificateDirectory, $"{signature.KeyId}.json");
		var certificateSignaturePath = Path.Combine(root, CertificateDirectory, $"{signature.KeyId}.sig");
		if (!File.Exists(certificatePath) || !File.Exists(certificateSignaturePath))
		{
			return VerifiedTree.Fail(RegistryRefreshError.CertificateUntrusted,
				$"The registry snapshot does not carry certificate '{signature.KeyId}'.");
		}

		var result = await RegistryManifestVerifier.VerifyAsync(manifestPath,
			signaturePath,
			await File.ReadAllBytesAsync(certificatePath, cancellationToken),
			await File.ReadAllBytesAsync(certificateSignaturePath, cancellationToken),
			_options.RootPublicKeyOverride,
			cancellationToken);

		if (!result.Success)
		{
			return VerifiedTree.Fail(MapSigningError(result.Error), result.Message ?? "Verification failed.");
		}

		return new VerifiedTree
		{
			Success = true,
			Sequence = manifest.Sequence,
			GeneratedAt = manifest.GeneratedAt,
			SignedAt = signature.SignedAt,
			CertificateId = result.CertificateId
		};
	}

	private string? RevokedSigningKey(string staging, string keyId)
	{
		// The incoming security.json is checked together with the copy already on disk, so a snapshot
		// cannot un-revoke the very key that signed it.
		if (_reader.ReadRevokedKeyIds(staging).Contains(keyId, StringComparer.OrdinalIgnoreCase))
		{
			return $"The registry signing key '{keyId}' is revoked.";
		}

		var current = _paths.StoreRegistryCurrentDirectory;
		if (Directory.Exists(current) &&
			_reader.ReadRevokedKeyIds(current).Contains(keyId, StringComparer.OrdinalIgnoreCase))
		{
			return $"The registry signing key '{keyId}' was revoked by the previously trusted snapshot.";
		}

		return null;
	}

	private bool Promote(string staging, out string? failure)
	{
		var current = _paths.StoreRegistryCurrentDirectory;
		var retired = current + ".old-" + Guid.CreateVersion7().ToString("N");
		failure = null;
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(current)!);
			var hadCurrent = Directory.Exists(current);
			if (hadCurrent)
			{
				Directory.Move(current, retired);
			}

			try
			{
				Directory.Move(staging, current);
			}
			catch (Exception)
			{
				if (hadCurrent && !Directory.Exists(current))
				{
					Directory.Move(retired, current);
				}

				throw;
			}

			SafeDelete(retired);
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			failure = ex.Message;
			return false;
		}
	}

	private bool IsStale(StoreRegistryStatus status)
	{
		if (!status.HasCatalog)
		{
			return false;
		}

		if (status.LastError is not null)
		{
			return true;
		}

		return status.SignedAt is { } signedAt && _timeProvider.GetUtcNow() - signedAt > _options.MaxSnapshotAge;
	}

	private HttpClient CreateClient(string name, TimeSpan timeout)
	{
		var client = _httpClientFactory.CreateClient(name);
		client.Timeout = timeout;
		return client;
	}

	private Task<StoreFetch> Fetch(HttpClient client,
		string relativePath,
		long maxBytes,
		long? expectedSize,
		CancellationToken cancellationToken)
		=> StoreHttp.Fetch(client,
			new Uri(_options.BaseUrl, relativePath),
			maxBytes,
			expectedSize,
			cancellationToken);

	private static bool IsSafeRelativePath(string path) =>
		!string.IsNullOrWhiteSpace(path) &&
		!path.StartsWith('/') &&
		!path.Contains("..", StringComparison.Ordinal) &&
		!path.Contains('\\', StringComparison.Ordinal) &&
		!Path.IsPathRooted(path);

	private static RegistryRefreshError MapSigningError(SigningError? error) => error switch
	{
		SigningError.CertificateUntrusted
			or SigningError.CertificateMalformed
			or SigningError.CertificateUnreadable => RegistryRefreshError.CertificateUntrusted,
		SigningError.CertificateWrongPurpose
			or SigningError.CertificateNotYetValid
			or SigningError.CertificateExpired => RegistryRefreshError.CertificateUntrusted,
		SigningError.FileDigestMismatch or SigningError.FileSizeMismatch => RegistryRefreshError.SizeMismatch,
		_ => RegistryRefreshError.SignatureInvalid
	};

	private static void SafeDelete(string directory)
	{
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			// A leftover staging or retired directory is reclaimed on the next refresh.
		}
	}

	public void Dispose() => _gate.Dispose();

	private sealed record VerifiedTree
	{
		public required bool Success { get; init; }

		public long Sequence { get; init; }

		public DateTimeOffset? GeneratedAt { get; init; }

		public DateTimeOffset? SignedAt { get; init; }

		public string? CertificateId { get; init; }

		public RegistryRefreshError? Error { get; init; }

		public string? FailureMessage { get; init; }

		public static VerifiedTree Fail(RegistryRefreshError error, string message) =>
			new() { Success = false, Error = error, FailureMessage = message };
	}
}
