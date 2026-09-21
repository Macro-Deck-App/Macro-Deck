using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Operations;
using MacroDeckHost.Application.Store.Reviews;
using MacroDeckHost.Application.Store.Testing;
using MacroDeckHost.Domain.Enums;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Store;

public sealed class StoreInstallExecutor : IStoreInstallExecutor
{
	private readonly IStoreCatalog _catalog;
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreOperationTracker _tracker;
	private readonly IStoreArtifactDownloader _downloader;
	private readonly IPluginInstaller _pluginInstaller;
	private readonly IIconPackCache _iconPackCache;
	private readonly IStoreInstallationStore _installations;
	private readonly StoreInstallConsent _consent;
	private readonly StoreInstallBackupBatches _backupBatches;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IMacroDeckPaths _paths;
	private readonly StoreRegistryOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	public StoreInstallExecutor(IStoreCatalog catalog,
		IStoreCatalogQueryService catalogQuery,
		IStoreOperationTracker tracker,
		IStoreArtifactDownloader downloader,
		IPluginInstaller pluginInstaller,
		IIconPackCache iconPackCache,
		IStoreInstallationStore installations,
		StoreInstallConsent consent,
		StoreInstallBackupBatches backupBatches,
		IServiceScopeFactory scopeFactory,
		IMacroDeckPaths paths,
		StoreRegistryOptions options,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_catalog = catalog;
		_catalogQuery = catalogQuery;
		_tracker = tracker;
		_downloader = downloader;
		_pluginInstaller = pluginInstaller;
		_iconPackCache = iconPackCache;
		_installations = installations;
		_consent = consent;
		_backupBatches = backupBatches;
		_scopeFactory = scopeFactory;
		_paths = paths;
		_options = options;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<StoreInstallExecutor>();
	}

	public async Task Execute(Guid operationId, CancellationToken cancellationToken = default)
	{
		var operation = _tracker.Find(operationId);
		if (operation is null || operation.IsTerminal)
		{
			return;
		}

		if (operation.Kind == StoreOperationKind.TestInstall)
		{
			await ExecuteTestBuild(operation, _consent.Consume(operationId), cancellationToken);
			return;
		}

		var snapshot = _catalog.Snapshot;
		if (snapshot.RemovedPackages.Any(removed =>
			string.Equals(removed.Id, operation.PackageId, StringComparison.OrdinalIgnoreCase)))
		{
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.PackageRemoved,
				"This package was removed from the registry.");
			return;
		}

		var found = _catalogQuery.Find(operation.ExtensionKind, operation.PackageId);
		if (!found.Success)
		{
			var error = found.Error == StoreCatalogError.RegistryUnavailable
				? StoreOperationError.RegistryUnavailable
				: StoreOperationError.PackageNotFound;
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				error,
				found.ErrorMessage ?? "The package could not be found.");
			return;
		}

		var item = found.Data!;
		if (item.InstallState == StoreInstallState.Unsupported)
		{
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.Unsupported,
				item.UnsupportedReason ?? "This extension is not supported on this platform.");
			return;
		}

		// Consumed for every kind, so a consent flag sent alongside an icon pack or profile template - both
		// registry-authenticated, with no signature path to consent to - is dropped rather than left
		// waiting to be picked up by something else.
		var consented = _consent.Consume(operationId);
		var backupBatchId = _backupBatches.Consume(operationId);

		try
		{
			switch (operation.ExtensionKind)
			{
				case StoreExtensionKind.Plugin:
					await ExecutePlugin(operationId,
						item.Entry,
						consented,
						backupBatchId,
						snapshot.Sequence,
						cancellationToken);
					break;
				case StoreExtensionKind.IconPack:
					await ExecuteIconPack(operationId, item.Entry, snapshot.Sequence, cancellationToken);
					break;
				case StoreExtensionKind.ProfileTemplate:
					await ExecuteProfileTemplate(operationId, item.Entry, snapshot.Sequence, cancellationToken);
					break;
				default:
					_tracker.Transition(operationId,
						StoreOperationState.Failed,
						StoreOperationError.Unsupported,
						$"'{operation.ExtensionKind}' cannot be installed.");
					break;
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			DeleteStagingDirectory(operationId);
			_tracker.Transition(operationId,
				StoreOperationState.Cancelled,
				StoreOperationError.Cancelled,
				"Cancelled.");
		}
		catch (Exception ex)
		{
			DeleteStagingDirectory(operationId);
			_logger.Error(ex,
				"Store install of {Kind} {PackageId} threw",
				operation.ExtensionKind,
				operation.PackageId);
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.InstallFailed,
				ex.Message);
		}
	}

	private async Task ExecutePlugin(Guid operationId,
		StoreCatalogEntry entry,
		bool consented,
		string? backupBatchId,
		long registrySequence,
		CancellationToken cancellationToken)
	{
		// Developer mode is read from the host's own preferences, never from the request: consent asked
		// for while it is off is a refusal, not a flag to ignore, so the user is told the package was
		// rejected instead of watching an install that was never going to be permitted.
		if (consented && !await DeveloperModeEnabled())
		{
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.UnsignedNotPermitted,
				"An unsigned plugin can only be installed from the store while Developer Mode is on.");
			return;
		}

		_tracker.Transition(operationId, StoreOperationState.Downloading);
		var progress = new Progress<PluginArtifactDownloadProgress>(sample =>
			_tracker.ReportProgress(operationId, sample.BytesRead, sample.TotalBytes));

		// The registry stores a bare hex digest, but PluginArtifactAcquirer expects the prefixed form
		// AssetContentHash produces everywhere else - the constant is used rather than a literal so the
		// two can never silently drift onto different formats.
		var expectedSha256 = AssetContentHash.Sha256Prefix + entry.LatestRelease.Sha256;
		var source = PluginArtifactSource.FromUrl(entry.LatestRelease.ArtifactUrl, expectedSha256)
			with
			{
				Progress = progress
			};
		var request = new PluginInstallRequest
		{
			AllowUnsigned = consented,
			Force = false,
			RetainDownload = false,
			BackupBatchId = backupBatchId,
			Stage = stage => _tracker.Transition(operationId,
				stage == PluginInstallStage.BackingUp
					? StoreOperationState.BackingUp
					: StoreOperationState.Installing)
		};

		var result = await _pluginInstaller.Install(source, request, cancellationToken);
		if (!result.Success)
		{
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				result.BlockedByDevelopmentTakeover
					? StoreOperationError.InstallBlockedByTakeover
					: MapPluginError(result.Error),
				result.ErrorMessage ?? "The plugin could not be installed.");
			return;
		}

		_installations.Save(new StoreInstallationRecord
		{
			Origin = _options.BaseUrl.ToString(),
			Kind = StoreExtensionKind.Plugin,
			PackageId = entry.Id,
			Version = string.IsNullOrEmpty(result.Version) ? entry.LatestVersion : result.Version,
			ArtifactSha256 = entry.LatestRelease.Sha256,
			DisplayName = entry.Name,
			RegistrySequence = registrySequence,
			InstalledAt = _timeProvider.GetUtcNow(),
			TargetIds = []
		});

		_tracker.Transition(operationId, StoreOperationState.Completed);
	}

	private async Task ExecuteTestBuild(StoreOperation operation,
		bool consented,
		CancellationToken cancellationToken)
	{
		var operationId = operation.Id;
		if (!consented)
		{
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.TestConsentRequired,
				"A test build is only installed after you confirm it.");
			return;
		}

		if (operation.TestBuildId is not { } buildId)
		{
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.TestBuildUnavailable,
				"The operation names no test build.");
			return;
		}

		try
		{
			using var scope = _scopeFactory.CreateScope();
			var platform = scope.ServiceProvider.GetRequiredService<IStorePlatformClient>();
			var testInstallations = scope.ServiceProvider.GetRequiredService<IStoreTestInstallationStore>();

			var download = await platform.GetTestBuildDownload(operation.PackageId, buildId, cancellationToken);
			if (!download.Success || download.Value is not { } link)
			{
				_tracker.Transition(operationId,
					StoreOperationState.Failed,
					download.Failure is StorePlatformFailure.SignInRequired or StorePlatformFailure.AccountSuspended
						? StoreOperationError.SignInRequired
						: StoreOperationError.TestBuildUnavailable,
					"The test build could not be requested from the Macro Deck Platform.");
				return;
			}

			_tracker.Transition(operationId, StoreOperationState.Downloading);
			var progress = new Progress<PluginArtifactDownloadProgress>(sample =>
				_tracker.ReportProgress(operationId, sample.BytesRead, sample.TotalBytes));
			var source = PluginArtifactSource.FromTestBuild(link.Url, AssetContentHash.Sha256Prefix + link.Sha256)
				with
				{
					Progress = progress
				};

			// ExpectedPluginId keeps a build of one plugin from being installed as another; Force replaces an
			// installed build of the same version.
			var request = new PluginInstallRequest
			{
				ExpectedPluginId = operation.PackageId,
				AllowUnsigned = true,
				Force = true,
				RetainDownload = false
			};
			var result = await _pluginInstaller.Install(source, request, cancellationToken);
			if (!result.Success)
			{
				_tracker.Transition(operationId,
					StoreOperationState.Failed,
					result.BlockedByDevelopmentTakeover
						? StoreOperationError.InstallBlockedByTakeover
						: result.Error == PluginInstallError.IdMismatch
							? StoreOperationError.TestBuildMismatch
							: MapPluginError(result.Error),
					result.ErrorMessage ?? "The test build could not be installed.");
				return;
			}

			testInstallations.Save(new StoreTestInstallationRecord
			{
				PluginId = operation.PackageId,
				Version = result.Version ?? operation.Version,
				Build = operation.TestBuild ?? string.Empty,
				BuildId = buildId,
				ArtifactSha256 = link.Sha256,
				InstalledAt = _timeProvider.GetUtcNow()
			});
			_tracker.Transition(operationId, StoreOperationState.Completed);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			_tracker.Transition(operationId,
				StoreOperationState.Cancelled,
				StoreOperationError.Cancelled,
				"Cancelled.");
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Test install of {PackageId} threw", operation.PackageId);
			_tracker.Transition(operationId,
				StoreOperationState.Failed,
				StoreOperationError.InstallFailed,
				ex.Message);
		}
	}

	private async Task ExecuteIconPack(Guid operationId,
		StoreCatalogEntry entry,
		long registrySequence,
		CancellationToken cancellationToken)
	{
		try
		{
			_tracker.Transition(operationId, StoreOperationState.Downloading);
			var progress = new Progress<StoreArtifactDownloadProgress>(sample =>
				_tracker.ReportProgress(operationId, sample.BytesRead, sample.TotalBytes));
			var download = await _downloader.Download(entry.LatestRelease.ArtifactUrl,
				entry.LatestRelease.Sha256,
				entry.LatestRelease.Size,
				operationId,
				progress,
				cancellationToken);
			if (!download.Success)
			{
				_tracker.Transition(operationId,
					StoreOperationState.Failed,
					MapDownloadError(download.Error!.Value),
					download.ErrorMessage ?? "The artifact could not be downloaded.");
				return;
			}

			_tracker.Transition(operationId, StoreOperationState.Installing);

			var existingRecord = _installations.Find(StoreExtensionKind.IconPack, entry.Id);
			var existingPack = existingRecord is { TargetIds.Count: > 0 }
				? _iconPackCache.GetPackById(existingRecord.TargetIds[0])
				: null;

			using var scope = _scopeFactory.CreateScope();
			var restoreService = scope.ServiceProvider.GetRequiredService<IIconPackRestoreService>();

			await using var stream = File.OpenRead(download.FilePath!);
			var fileName = $"{entry.Id}.macroDeckIconPack";
			var restoreResult = existingPack is not null
				? await restoreService.UpgradePack(existingPack.Id, fileName, stream, cancellationToken)
				: await restoreService.RestoreAsNewPack(fileName, stream, cancellationToken);

			if (!restoreResult.Success)
			{
				_tracker.Transition(operationId,
					StoreOperationState.Failed,
					StoreOperationError.MalformedPackage,
					"The icon pack archive could not be installed.");
				return;
			}

			var pack = restoreResult.Data!;
			pack.SourceType = IconPackSourceType.ExtensionStore;
			pack.SourceId = entry.Id;
			pack.Version = entry.LatestVersion;
			await _iconPackCache.AddOrUpdatePack(pack);

			_installations.Save(new StoreInstallationRecord
			{
				Origin = _options.BaseUrl.ToString(),
				Kind = StoreExtensionKind.IconPack,
				PackageId = entry.Id,
				Version = entry.LatestVersion,
				ArtifactSha256 = entry.LatestRelease.Sha256,
				DisplayName = entry.Name,
				RegistrySequence = registrySequence,
				InstalledAt = _timeProvider.GetUtcNow(),
				TargetIds = [pack.Id]
			});

			_tracker.Transition(operationId, StoreOperationState.Completed);

			// Restore announced the pack before the installation record existed, so clients still
			// describe it as user-owned and editable until they hear it is Store-owned.
			try
			{
				await scope.ServiceProvider.GetRequiredService<IMediator>()
					.Publish(new IconPackUpdatedNotification(pack, _iconPackCache.GetIconCount(pack.Id)),
						CancellationToken.None);
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Failed to announce installed icon pack {PackId}", pack.Id);
			}
		}
		finally
		{
			DeleteStagingDirectory(operationId);
		}
	}

	private async Task ExecuteProfileTemplate(Guid operationId,
		StoreCatalogEntry entry,
		long registrySequence,
		CancellationToken cancellationToken)
	{
		try
		{
			_tracker.Transition(operationId, StoreOperationState.Downloading);
			var progress = new Progress<StoreArtifactDownloadProgress>(sample =>
				_tracker.ReportProgress(operationId, sample.BytesRead, sample.TotalBytes));
			var download = await _downloader.Download(entry.LatestRelease.ArtifactUrl,
				entry.LatestRelease.Sha256,
				entry.LatestRelease.Size,
				operationId,
				progress,
				cancellationToken);
			if (!download.Success)
			{
				_tracker.Transition(operationId,
					StoreOperationState.Failed,
					MapDownloadError(download.Error!.Value),
					download.ErrorMessage ?? "The artifact could not be downloaded.");
				return;
			}

			_tracker.Transition(operationId, StoreOperationState.Installing);
			var bytes = await File.ReadAllBytesAsync(download.FilePath!, cancellationToken);

			using var scope = _scopeFactory.CreateScope();
			var portability = scope.ServiceProvider.GetRequiredService<IProfilePortabilityService>();
			var importResult = await portability.Import(bytes, password: null, cancellationToken);
			if (!importResult.Success)
			{
				_tracker.Transition(operationId,
					StoreOperationState.Failed,
					StoreOperationError.MalformedPackage,
					"The profile template archive could not be installed.");
				return;
			}

			var profile = importResult.Data!;
			_installations.Save(new StoreInstallationRecord
			{
				Origin = _options.BaseUrl.ToString(),
				Kind = StoreExtensionKind.ProfileTemplate,
				PackageId = entry.Id,
				Version = entry.LatestVersion,
				ArtifactSha256 = entry.LatestRelease.Sha256,
				DisplayName = entry.Name,
				RegistrySequence = registrySequence,
				InstalledAt = _timeProvider.GetUtcNow(),
				TargetIds = [profile.Id]
			});

			_tracker.Transition(operationId, StoreOperationState.Completed);
		}
		finally
		{
			DeleteStagingDirectory(operationId);
		}
	}

	private async Task<bool> DeveloperModeEnabled()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		var developer = await preferences.GetDeveloper();
		return developer.Enabled;
	}

	private void DeleteStagingDirectory(Guid operationId)
	{
		var directory = Path.Combine(_paths.StoreStagingDirectory, operationId.ToString("N"));
		try
		{
			if (Directory.Exists(directory))
			{
				Directory.Delete(directory, recursive: true);
			}
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Failed to delete store staging directory {Directory}", directory);
		}
	}

	private static StoreOperationError MapPluginError(PluginInstallError? error) => error switch
	{
		PluginInstallError.HashMismatch => StoreOperationError.ChecksumMismatch,
		PluginInstallError.SignatureInvalid => StoreOperationError.SignatureInvalid,
		PluginInstallError.SignatureUntrusted
			or PluginInstallError.SignatureRevoked
			or PluginInstallError.SignatureUnverifiable => StoreOperationError.SignatureUntrusted,
		PluginInstallError.UnsignedNotPermitted => StoreOperationError.UnsignedNotPermitted,
		PluginInstallError.TrustDowngrade => StoreOperationError.TrustDowngrade,
		PluginInstallError.Incompatible => StoreOperationError.Incompatible,
		PluginInstallError.InvalidArchive
			or PluginInstallError.ManifestMissing
			or PluginInstallError.ManifestInvalid
			or PluginInstallError.UnsafeEntry =>
			StoreOperationError.MalformedPackage,
		PluginInstallError.ArtifactTooLarge => StoreOperationError.ArtifactTooLarge,
		_ => StoreOperationError.InstallFailed
	};

	private static StoreOperationError MapDownloadError(StoreArtifactDownloadError error) => error switch
	{
		StoreArtifactDownloadError.DownloadFailed => StoreOperationError.DownloadFailed,
		StoreArtifactDownloadError.SizeMismatch => StoreOperationError.SizeMismatch,
		StoreArtifactDownloadError.ChecksumMismatch => StoreOperationError.ChecksumMismatch,
		StoreArtifactDownloadError.ArtifactTooLarge => StoreOperationError.ArtifactTooLarge,
		_ => StoreOperationError.DownloadFailed
	};
}
