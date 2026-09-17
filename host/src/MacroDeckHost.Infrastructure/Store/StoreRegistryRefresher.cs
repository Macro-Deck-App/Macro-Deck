using System.Net;
using System.Text.Json;
using MacroDeck.Signing;
using MacroDeck.Signing.Registry;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Domain.Common;
using Mediator;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreRegistryRefresher : IStoreRegistryRefresher, IDisposable
{
	private const string ManifestFileName = "registry-manifest.json";
	private const string SignatureFileName = "registry-signature.json";
	private const string CertificateDirectory = "certificates";

	private static readonly TimeSpan _disposeWait = TimeSpan.FromSeconds(5);

	private readonly SemaphoreSlim _gate = new(1, 1);
	private readonly Lock _inflightLock = new();
	private readonly CancellationTokenSource _lifetime;
	private readonly IHttpClientFactory _httpClientFactory;
	private readonly StoreRegistryOptions _options;
	private readonly IStoreRegistryStateStore _stateStore;
	private readonly StoreRegistryReader _reader;
	private readonly IStoreCatalog _catalog;
	private readonly IMacroDeckPaths _paths;
	private readonly TimeProvider _timeProvider;
	private readonly IStoreRegistryRefreshTracker _tracker;
	private readonly IMediator _mediator;
	private readonly ILogger _logger;

	private StoreRegistryStatus _status = StoreRegistryStatus.Unavailable;
	private Task<Result<RegistryRefreshError>>? _inflight;

	public StoreRegistryRefresher(IHttpClientFactory httpClientFactory,
		StoreRegistryOptions options,
		IStoreRegistryStateStore stateStore,
		StoreRegistryReader reader,
		IStoreCatalog catalog,
		IMacroDeckPaths paths,
		TimeProvider timeProvider,
		IStoreRegistryRefreshTracker tracker,
		IMediator mediator,
		IHostApplicationLifetime lifetime,
		ILogger logger)
	{
		_httpClientFactory = httpClientFactory;
		_options = options;
		_stateStore = stateStore;
		_reader = reader;
		_catalog = catalog;
		_paths = paths;
		_timeProvider = timeProvider;
		_tracker = tracker;
		_mediator = mediator;
		_lifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);
		_logger = logger.ForContext<StoreRegistryRefresher>();
	}

	public StoreRegistryStatus Status
	{
		get
		{
			lock (_inflightLock)
			{
				return _status with { Stale = IsStale(_status), Refreshing = _inflight is not null };
			}
		}
	}

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

		if (_status.HasCatalog)
		{
			await AnnounceOutcome();
		}
	}

	public Task<Result<RegistryRefreshError>> Refresh(CancellationToken cancellationToken = default) =>
		Refresh(StoreRegistryRefreshTrigger.Manual, cancellationToken);

	public Task<Result<RegistryRefreshError>> Refresh(StoreRegistryRefreshTrigger trigger,
		CancellationToken cancellationToken = default)
	{
		lock (_inflightLock)
		{
			if (_inflight is null)
			{
				_tracker.Begin(trigger);
				// Task.Run so the run's finally, which clears the slot under this lock, cannot execute before
				// the slot is assigned. The run uses the refresher's own token: one caller leaving must not stop it.
				var token = _lifetime.Token;
				_inflight = Task.Run(() => RunShared(token), CancellationToken.None);
			}

			return _inflight.WaitAsync(cancellationToken);
		}
	}

	public void Dispose()
	{
		_lifetime.Cancel();
		Task? inflight;
		lock (_inflightLock)
		{
			inflight = _inflight;
		}

		if (inflight is not null && !WaitQuietly(inflight))
		{
			return;
		}

		_lifetime.Dispose();
		_gate.Dispose();
	}

	private async Task<Result<RegistryRefreshError>> RunShared(CancellationToken cancellationToken)
	{
		var state = StoreRegistryRefreshRunState.Failed;
		RegistryRefreshError? error = null;
		string? detail = null;
		try
		{
			await _gate.WaitAsync(cancellationToken);
			try
			{
				var result = await RefreshUntilConsistent(cancellationToken);
				// StoreHttp reports a cancelled fetch as a network failure; a stopping host is not a failed refresh.
				if (!result.Success)
				{
					cancellationToken.ThrowIfCancellationRequested();
				}

				_status = _status with
				{
					LastAttemptAt = _timeProvider.GetUtcNow(),
					LastError = result.Success ? null : result.Error,
					LastErrorMessage = result.Success ? null : result.ErrorMessage
				};
				state = result.Success ? StoreRegistryRefreshRunState.Succeeded : StoreRegistryRefreshRunState.Failed;
				error = result.Error;
				detail = result.ErrorMessage;
				return result;
			}
			finally
			{
				_gate.Release();
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			state = StoreRegistryRefreshRunState.Cancelled;
			throw;
		}
		catch (Exception ex)
		{
			detail = ex.Message;
			throw;
		}
		finally
		{
			lock (_inflightLock)
			{
				_inflight = null;
				_tracker.Finish(state, error, detail, _status with { Stale = IsStale(_status), Refreshing = false });
			}

			if (state is not StoreRegistryRefreshRunState.Cancelled)
			{
				await AnnounceOutcome();
			}
		}
	}

	private async Task<Result<RegistryRefreshError>> RefreshUntilConsistent(CancellationToken cancellationToken)
	{
		for (var attempt = 0;; attempt++)
		{
			var staging = Path.Combine(_paths.StoreRegistryStagingDirectory, Guid.CreateVersion7().ToString("N"));
			Attempt outcome;
			try
			{
				outcome = await RefreshCore(staging, cancellationToken);
			}
			finally
			{
				SafeDelete(staging);
			}

			// Every attempt verifies from scratch, so waiting only defers a failure and never admits anything
			// unverified; a mismatch that outlasts the waits fails with its own error as before.
			if (outcome.Result.Success ||
				!outcome.MayBeUpdateRace ||
				attempt >= _options.UpdateRaceRetryDelays.Count ||
				cancellationToken.IsCancellationRequested)
			{
				return outcome.Result;
			}

			var delay = _options.UpdateRaceRetryDelays[attempt];
			_tracker.Log(StoreRegistryRefreshStep.WaitingForRegistryUpdate,
				count: (int)Math.Ceiling(delay.TotalSeconds));
			await Task.Delay(delay, _timeProvider, cancellationToken);
		}
	}

	private async Task AnnounceOutcome()
	{
		try
		{
			await _mediator.Publish(new StoreRegistryRefreshedNotification(Status), CancellationToken.None);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Announcing the store registry refresh outcome failed.");
		}
	}

	private static bool WaitQuietly(Task task)
	{
		try
		{
			return task.Wait(_disposeWait);
		}
		catch (AggregateException)
		{
			return true;
		}
	}

	private async Task<Attempt> RefreshCore(string staging,
		CancellationToken cancellationToken)
	{
		Directory.CreateDirectory(staging);
		using var client = CreateClient(StoreHttp.RegistryClientName, _options.RequestTimeout);

		_tracker.Log(StoreRegistryRefreshStep.FetchingManifest);
		var manifestFetch = await Fetch(client,
			ManifestFileName,
			_options.MaxRegistryFileBytes,
			null,
			cancellationToken);
		if (!manifestFetch.Success)
		{
			return Result.Fail(RegistryRefreshError.NetworkFailure, manifestFetch.FailureMessage);
		}

		_tracker.Log(StoreRegistryRefreshStep.FetchingSignature);
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
			return new Attempt(Result.Fail(RegistryRefreshError.SequenceRollback,
					$"The registry offered sequence {manifest.Sequence}, below the accepted {accepted}."),
				MayBeUpdateRace: true);
		}

		if (manifest.Sequence == accepted &&
			string.Equals(state?.ManifestSha256, manifestFetch.Sha256, StringComparison.OrdinalIgnoreCase) &&
			_status.HasCatalog)
		{
			_stateStore.Save(state! with { LastSuccessAt = _timeProvider.GetUtcNow() });
			_status = _status with { LastSuccessAt = _timeProvider.GetUtcNow() };
			_tracker.Log(StoreRegistryRefreshStep.UpToDate, sequence: manifest.Sequence);
			return Result.Ok<RegistryRefreshError>();
		}

		if (manifest.Files.Count > _options.MaxRegistryFiles ||
			manifest.Files.Sum(file => file.Size) > _options.MaxRegistryTotalBytes)
		{
			return Result.Fail(RegistryRefreshError.BudgetExceeded,
				"The registry manifest declares more data than this host will download.");
		}

		_tracker.Log(StoreRegistryRefreshStep.DownloadingFiles, count: manifest.Files.Count);
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

		_tracker.Log(StoreRegistryRefreshStep.Verifying);
		var verified = await VerifyTree(staging, accepted, cancellationToken);
		if (!verified.Success)
		{
			return new Attempt(Result.Fail(verified.Error!.Value, verified.FailureMessage),
				verified.CertificateMissing ||
				verified.Error is RegistryRefreshError.SizeMismatch or RegistryRefreshError.SignatureInvalid);
		}

		if (RevokedSigningKey(staging, signature.KeyId) is { } revokedReason)
		{
			return Result.Fail(RegistryRefreshError.SigningKeyRevoked, revokedReason);
		}

		_tracker.Log(StoreRegistryRefreshStep.ReadingCatalog);
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

		_tracker.Log(StoreRegistryRefreshStep.Applied, sequence: snapshot.Sequence);
		return Result.Ok<RegistryRefreshError>();
	}

	private async Task<Attempt?> DownloadDeclaredFiles(HttpClient client,
		RegistryManifestDocument manifest,
		string staging,
		CancellationToken cancellationToken)
	{
		var completed = 0;
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
				return new Attempt(Result.Fail(fetch.SizeMismatch
							? RegistryRefreshError.SizeMismatch
							: RegistryRefreshError.NetworkFailure,
						fetch.FailureMessage),
					fetch.SizeMismatch || fetch.StatusCode == (int)HttpStatusCode.NotFound);
			}

			var destination = Path.Combine(staging, Path.Combine(file.Path.Split('/')));
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			await File.WriteAllBytesAsync(destination, fetch.Content!, cancellationToken);
			_tracker.ReportProgress(++completed, manifest.Files.Count);
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
				$"The registry snapshot does not carry certificate '{signature.KeyId}'.") with { CertificateMissing = true };
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

	private sealed record VerifiedTree
	{
		public required bool Success { get; init; }

		public long Sequence { get; init; }

		public DateTimeOffset? GeneratedAt { get; init; }

		public DateTimeOffset? SignedAt { get; init; }

		public string? CertificateId { get; init; }

		public RegistryRefreshError? Error { get; init; }

		public string? FailureMessage { get; init; }

		public bool CertificateMissing { get; init; }

		public static VerifiedTree Fail(RegistryRefreshError error, string message) =>
			new() { Success = false, Error = error, FailureMessage = message };
	}

	private sealed record Attempt(Result<RegistryRefreshError> Result, bool MayBeUpdateRace)
	{
		public static implicit operator Attempt(Result<RegistryRefreshError> result) => new(result, false);
	}
}
