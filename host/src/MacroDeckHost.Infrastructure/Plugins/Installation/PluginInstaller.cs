using MacroDeckHost.Application.Backups;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using MacroDeck.Plugin.Analyzers;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Plugins.Trust;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Infrastructure.Plugins.Installation;

public sealed class PluginInstaller : IPluginInstaller
{
	private const int DeleteAttempts = 3;

	private const long MaxInlineIconBytes = 512 * 1024;

	private static readonly TimeSpan _deleteRetryDelay = TimeSpan.FromMilliseconds(200);

	private static readonly TimeSpan _healthPollInterval = TimeSpan.FromMilliseconds(250);

	private readonly ConcurrentDictionary<string, SemaphoreSlim> _pluginGates = new(StringComparer.Ordinal);

	private readonly IMacroDeckPaths _paths;
	private readonly IPluginArtifactReader _artifactReader;
	private readonly IPluginArtifactAcquirer _acquirer;
	private readonly IPluginArtifactCache _cache;
	private readonly IPluginTrustEvaluator _trustEvaluator;
	private readonly IPluginDependencyResolver _dependencyResolver;
	private readonly IPluginManifestReader _manifestReader;
	private readonly IPluginInstallationCatalog _catalog;
	private readonly IPluginSupervisor _supervisor;
	private readonly IRemotePluginIntegrationRegistrar _integrationRegistrar;
	private readonly IPluginSessionRegistry _sessionRegistry;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly PluginInstallerOptions _options;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly DurableJsonFile _currentVersionFiles;

	public PluginInstaller(IMacroDeckPaths paths,
		IPluginArtifactReader artifactReader,
		IPluginArtifactAcquirer acquirer,
		IPluginArtifactCache cache,
		IPluginTrustEvaluator trustEvaluator,
		IPluginDependencyResolver dependencyResolver,
		IPluginManifestReader manifestReader,
		IPluginInstallationCatalog catalog,
		IPluginSupervisor supervisor,
		IRemotePluginIntegrationRegistrar integrationRegistrar,
		IPluginSessionRegistry sessionRegistry,
		IServiceScopeFactory scopeFactory,
		PluginInstallerOptions options,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_paths = paths;
		_artifactReader = artifactReader;
		_acquirer = acquirer;
		_cache = cache;
		_trustEvaluator = trustEvaluator;
		_dependencyResolver = dependencyResolver;
		_manifestReader = manifestReader;
		_catalog = catalog;
		_supervisor = supervisor;
		_integrationRegistrar = integrationRegistrar;
		_sessionRegistry = sessionRegistry;
		_scopeFactory = scopeFactory;
		_options = options;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<PluginInstaller>();
		_currentVersionFiles = new DurableJsonFile("plugin version pointer", PersistenceJsonOptions.Default, _logger);
	}

	public async Task<PluginInstallResult> Inspect(PluginArtifactSource source,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);

		var stagingDirectory = CreateStagingDirectory();
		try
		{
			var acquisition = await _acquirer.Acquire(source, stagingDirectory, cancellationToken);
			if (!acquisition.Success)
			{
				return PluginInstallResult.Fail(acquisition.Error ?? PluginInstallError.Failed,
					acquisition.ErrorMessage ?? "The artifact could not be read.");
			}

			var inspection = await _artifactReader.Inspect(acquisition.ArtifactPath!, cancellationToken);
			if (!inspection.Success || inspection.Manifest is not { } manifest)
			{
				return PluginInstallResult.Fail(inspection.Error ?? PluginInstallError.Failed,
					inspection.ErrorMessage ?? "The artifact could not be read.");
			}

			if (Incompatibility(manifest) is { } incompatibility)
			{
				return PluginInstallResult.Fail(PluginInstallError.Incompatible,
					incompatibility,
					manifest.Id,
					manifest.Version);
			}

			var extractDirectory = Path.Combine(stagingDirectory, "extract");
			var extraction = await _artifactReader.ExtractTo(acquisition.ArtifactPath!,
				extractDirectory,
				cancellationToken);

			if (!extraction.Success)
			{
				return PluginInstallResult.Fail(extraction.Error ?? PluginInstallError.Failed,
						extraction.ErrorMessage ?? "The artifact could not be extracted.",
						manifest.Id,
						manifest.Version) with
					{
						Publisher = manifest.Publisher
					};
			}

			if (VerifyDigests(manifest, extraction.ExtractedFiles, extractDirectory) is { } digestFailure)
			{
				return PluginInstallResult.Fail(PluginInstallError.HashMismatch,
						digestFailure,
						manifest.Id,
						manifest.Version) with
					{
						Publisher = manifest.Publisher
					};
			}

			var trust = await _trustEvaluator.EvaluateInstalledAsync(extractDirectory, cancellationToken);
			var warnings = CollectWarnings(manifest, trust);

			// Blocking here only ever colours this read-only preview for the caller - Inspect never gates
			// an install, and this warning is never carried into Install's own CollectWarnings call, so it
			// cannot hold back activation of a package that a real Install would otherwise permit.
			//
			// Unsigned is excluded: CollectWarnings above already added its own Advisory for it, and with
			// consent an unsigned install both activates and starts, so a Blocking warning here would tell
			// the confirm modal to render "will be installed but will not start" about a plugin that will.
			if (!trust.IsTrusted && trust.Verdict != PluginTrustVerdict.Unsigned)
			{
				warnings.Add(PluginInstallWarning.Blocking(PluginDependencyWarningCodes.NotTrusted,
					trust.Message ?? $"The artifact's signature verifies as {trust.Verdict}."));
			}

			return PluginInstallResult.Ok(manifest.Id,
					manifest.Version,
					ActiveVersionOf(manifest.Id),
					activated: false,
					warnings) with
				{
					Publisher = manifest.Publisher,
					Signature = trust,
					SignedWith = manifest.Signature,
					Name = manifest.Name,
					Description = manifest.Description,
					IconDataUri = ReadIconDataUri(manifest, extractDirectory),
					SupportedOnThisPlatform = HasEntrypointForThisHost(manifest)
				};
		}
		catch (OperationCanceledException)
		{
			return PluginInstallResult.Fail(PluginInstallError.Cancelled, "The inspection was cancelled.");
		}
		finally
		{
			DeleteDirectory(stagingDirectory);
		}
	}

	public async Task<PluginInstallResult> Install(PluginArtifactSource source,
		PluginInstallRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(request);

		var stagingDirectory = CreateStagingDirectory();
		PluginArtifactAcquisition? acquisition = null;

		try
		{
			acquisition = await _acquirer.Acquire(source, stagingDirectory, cancellationToken);
			if (!acquisition.Success)
			{
				return PluginInstallResult.Fail(acquisition.Error ?? PluginInstallError.Failed,
					acquisition.ErrorMessage ?? "The artifact could not be acquired.");
			}

			var inspection = await _artifactReader.Inspect(acquisition.ArtifactPath!, cancellationToken);
			if (!inspection.Success || inspection.Manifest is not { } manifest)
			{
				return PluginInstallResult.Fail(inspection.Error ?? PluginInstallError.Failed,
					inspection.ErrorMessage ?? "The artifact could not be read.");
			}

			if (request.ExpectedPluginId is { } expectedId &&
				!string.Equals(expectedId, manifest.Id, StringComparison.Ordinal))
			{
				return PluginInstallResult.Fail(PluginInstallError.IdMismatch,
					$"The artifact contains '{manifest.Id}' but '{expectedId}' was requested.",
					manifest.Id,
					manifest.Version);
			}

			if (Incompatibility(manifest) is { } incompatibility)
			{
				return PluginInstallResult.Fail(PluginInstallError.Incompatible,
					incompatibility,
					manifest.Id,
					manifest.Version);
			}

			var gate = _pluginGates.GetOrAdd(manifest.Id, _ => new SemaphoreSlim(1, 1));
			await gate.WaitAsync(cancellationToken);
			try
			{
				if (await RefuseWhenPreUpdateBackupFails(manifest.Id, cancellationToken) is { } refusal)
				{
					return refusal;
				}

				return await InstallLocked(manifest, acquisition, request, stagingDirectory, cancellationToken);
			}
			finally
			{
				gate.Release();
			}
		}
		catch (OperationCanceledException)
		{
			return PluginInstallResult.Fail(PluginInstallError.Cancelled, "The installation was cancelled.");
		}
		finally
		{
			DeleteDirectory(stagingDirectory);
		}
	}

	/// <summary>
	/// Backs the installation up before an existing plugin is replaced. A first-time install has nothing to
	/// lose and is not backed up; a failed backup refuses the install rather than replacing data the user
	/// asked to have a copy of first.
	/// </summary>
	private async Task<PluginInstallResult?> RefuseWhenPreUpdateBackupFails(string pluginId,
		CancellationToken cancellationToken)
	{
		if (!_catalog.TryResolveActive(pluginId, out _))
		{
			return null;
		}

		await using var scope = _scopeFactory.CreateAsyncScope();
		var coordinator = scope.ServiceProvider.GetService<IPreUpdateBackupCoordinator>();
		if (coordinator is null)
		{
			return null;
		}

		if (coordinator.IsRestorePending)
		{
			return PluginInstallResult.Fail(PluginInstallError.Failed,
				"A restore is waiting to be applied. Restart Macro Deck before updating plugins.",
				pluginId);
		}

		var outcome = await coordinator.EnsureBeforePluginUpdate(pluginId, batchId: null, cancellationToken);
		if (outcome.Success || outcome.Skipped)
		{
			return null;
		}

		return PluginInstallResult.Fail(PluginInstallError.Failed,
			$"The backup before updating '{pluginId}' failed, so the plugin was not updated. " +
			(outcome.Reason ?? "The backup could not be created."),
			pluginId);
	}

	public async Task<PluginInstallResult> Activate(string pluginId,
		string version,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
		ArgumentException.ThrowIfNullOrWhiteSpace(version);

		if (!TryResolveInstalledVersion(pluginId, version, out var manifest))
		{
			return PluginInstallResult.Fail(PluginInstallError.NotInstalled,
				$"Plugin '{pluginId}' has no installed version {version}.",
				pluginId,
				version);
		}

		var gate = _pluginGates.GetOrAdd(pluginId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			var versionDirectory = PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, pluginId, version);
			var trust = await _trustEvaluator.EvaluateInstalledAsync(versionDirectory, cancellationToken);
			var decision = await EvaluateTrustGate(pluginId, version, trust, cancellationToken);

			if (!decision.Permitted)
			{
				return PluginInstallResult.Fail(ToActivationError(trust),
						decision.RefusalReason ?? "The installed plugin could not be trusted.",
						pluginId,
						version)
					with
					{
						Signature = trust
					};
			}

			await ApplyTrustGateAction(pluginId, version, decision.Action, trust);

			var warnings = CollectWarnings(manifest, trust);

			var result = await ActivateAndValidate(pluginId,
				version,
				warnings,
				HasEntrypointForThisHost(manifest),
				PluginRollbackPlan.None,
				cancellationToken);

			return result with { Signature = trust };
		}
		catch (OperationCanceledException)
		{
			return PluginInstallResult.Fail(PluginInstallError.Cancelled, "The activation was cancelled.");
		}
		finally
		{
			gate.Release();
		}
	}

	public async Task<PluginInstallResult> Uninstall(string pluginId,
		PluginUninstallRequest request,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);
		ArgumentNullException.ThrowIfNull(request);

		// The catalog only ever yields well-formed ids, so checking it used to be what kept a caller's id
		// out of the paths built below. This method no longer requires a catalog entry, so the id has to
		// be validated in its own right before it reaches a recursive delete.
		if (!PluginId.IsValid(pluginId))
		{
			return PluginInstallResult.Fail(PluginInstallError.NotInstalled,
				$"Plugin '{pluginId}' is not installed.",
				pluginId);
		}

		var gate = _pluginGates.GetOrAdd(pluginId, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(cancellationToken);
		try
		{
			var installed = _catalog.Discover()
				.FirstOrDefault(plugin => string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal));

			var pluginDirectory = PluginInstallPaths.PluginDirectory(_paths.PluginsDirectory, pluginId);
			var versionsDirectory = PluginInstallPaths.VersionsDirectory(_paths.PluginsDirectory, pluginId);
			var currentPath = PluginInstallPaths.CurrentFilePath(_paths.PluginsDirectory, pluginId);

			// An uninstall is a cleanup, not a lookup: a hand-deleted plugin folder, a half-finished
			// uninstall and a crash between removing the files and retiring the rest all leave a plugin
			// whose files no longer describe it but whose host-side state still does, and refusing those
			// on "not installed" is what strands them. The residue that decides this is deliberately
			// narrow - a directory, or a trust record, both of which only an installed artifact produces.
			// A registration row and a registered adapter are what a self-registered plugin has without
			// ever having been installed, so counting either would turn an uninstall into a way to revoke
			// a developer build's enrollment.
			if (!await AnyInstallationResidue(installed, pluginDirectory, pluginId))
			{
				return PluginInstallResult.Fail(PluginInstallError.NotInstalled,
					$"Plugin '{pluginId}' is not installed.",
					pluginId);
			}

			if (!request.Force)
			{
				var dependents = _dependencyResolver.FindHardDependents(pluginId);
				if (dependents.Count > 0)
				{
					return PluginInstallResult.Fail(PluginInstallError.DependencyInUse,
						$"'{pluginId}' is required by {string.Join(", ", dependents)}.",
						pluginId);
				}
			}

			await _supervisor.Stop(pluginId, PluginStopReason.UserRequested, cancellationToken);

			if (!TryDeleteDirectoryWithRetries(versionsDirectory))
			{
				return PluginInstallResult.Fail(PluginInstallError.Failed,
					$"Could not remove '{versionsDirectory}'; a file there is still in use.",
					pluginId);
			}

			_currentVersionFiles.Delete(currentPath);

			// KeepData keeps the plugin's data, not an empty shell: a directory with nothing left in it
			// still reports as an installed plugin to the catalog, and with it to the installed-plugin
			// list the Integrations page reads.
			if (!request.KeepData || IsEmptyDirectory(pluginDirectory))
			{
				TryDeleteDirectoryWithRetries(pluginDirectory);
			}

			_catalog.Invalidate();

			await RetireRegistration(pluginId);

			await DeleteTrustRecordsForPlugin(pluginId);

			await _supervisor.Forget(pluginId, cancellationToken);

			await _integrationRegistrar.UnregisterAsync(pluginId, cancellationToken);

			PluginInstallerLog.Uninstalled(_logger, pluginId, request.KeepData);
			return PluginInstallResult.Ok(pluginId,
				installed?.ActiveVersion?.Version ?? string.Empty,
				previousVersion: null,
				activated: false);
		}
		catch (OperationCanceledException)
		{
			return PluginInstallResult.Fail(PluginInstallError.Cancelled, "The uninstall was cancelled.");
		}
		finally
		{
			gate.Release();
		}
	}

	private static string? ReadIconDataUri(PluginManifest manifest, string extractDirectory)
	{
		if (string.IsNullOrWhiteSpace(manifest.Icon))
		{
			return null;
		}

		var mediaType = IconMediaTypes.ForExtension(manifest.Icon);

		if (mediaType is null)
		{
			return null;
		}

		try
		{
			var root = Path.GetFullPath(extractDirectory);
			var iconPath = Path.GetFullPath(Path.Combine(root, manifest.Icon));

			if (!iconPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			{
				return null;
			}

			var file = new FileInfo(iconPath);
			if (!file.Exists || file.Length == 0 || file.Length > MaxInlineIconBytes)
			{
				return null;
			}

			return $"data:{mediaType};base64,{Convert.ToBase64String(File.ReadAllBytes(iconPath))}";
		}
		catch (Exception exception) when (exception is IOException
			or UnauthorizedAccessException
			or ArgumentException
			or NotSupportedException)
		{
			return null;
		}
	}

	private async Task<bool> AnyInstallationResidue(InstalledPlugin? installed,
		string pluginDirectory,
		string pluginId)
	{
		if (installed is not null || Directory.Exists(pluginDirectory))
		{
			return true;
		}

		using var scope = _scopeFactory.CreateScope();
		var trustRecords = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		return (await trustRecords.GetForPlugin(pluginId)).Count > 0;
	}

	private static bool IsEmptyDirectory(string path)
	{
		try
		{
			return Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any();
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}

	private async Task RetireRegistration(string pluginId)
	{
		using var scope = _scopeFactory.CreateScope();
		var registrations = scope.ServiceProvider.GetRequiredService<IPluginRegistrationService>();
		await registrations.Revoke(pluginId);
	}

	private async Task RevokeRegistrationRow(string pluginId)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IPluginRegistrationRepository>();
		await repository.Revoke(pluginId, _timeProvider.GetUtcNow().UtcDateTime);
	}

	// Read here rather than taken from the request: developer mode is the host's own state, and a caller
	// that could assert it would turn the url-source consent path into a client-supplied flag.
	private async Task<bool> DeveloperModeEnabled()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
		var developer = await preferences.GetDeveloper();
		return developer.Enabled;
	}

	private async Task<PluginTrustVerdict?> GetHighestAdmittedTier(string pluginId)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		var records = await repository.GetForPlugin(pluginId);

		var verdicts = records
			.Select(record => PluginTrustRecordVerdicts.Parse(record.AdmittedVerdict))
			.ToList();

		if (verdicts.Count == 0)
		{
			return null;
		}

		return verdicts.Contains(PluginTrustVerdict.Trusted)
			? PluginTrustVerdict.Trusted
			: PluginTrustVerdict.Unsigned;
	}

	private async Task UpsertTrustRecord(string pluginId,
		string version,
		PluginTrustVerdict verdict,
		string? certificateId)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		await repository.Upsert(pluginId,
			version,
			PluginTrustRecordVerdicts.From(verdict),
			certificateId,
			_timeProvider.GetUtcNow().UtcDateTime);
	}

	private async Task DeleteTrustRecord(string pluginId, string version)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		await repository.DeleteVersion(pluginId, version);
	}

	private async Task DeleteTrustRecordsForPlugin(string pluginId)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		await repository.DeleteForPlugin(pluginId);
	}

	private async Task DeleteTrustRecordVersion(string pluginId, string version)
	{
		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		await repository.DeleteVersion(pluginId, version);
	}

	private async Task<PluginTrustGateDecision> EvaluateTrustGate(string pluginId,
		string version,
		PluginTrustResult onDisk,
		CancellationToken cancellationToken)
	{
		using var scope = _scopeFactory.CreateScope();
		var trustRecords = scope.ServiceProvider.GetRequiredService<IPluginTrustRecordRepository>();
		var baseline = scope.ServiceProvider.GetRequiredService<IPluginTrustBaseline>();

		var record = await trustRecords.GetVersion(pluginId, version);
		var baselineExists = await baseline.Exists(cancellationToken);

		return PluginTrustGate.Evaluate(PluginTrustRecordVerdicts.Parse(record?.AdmittedVerdict),
			record?.CertificateId,
			onDisk,
			baselineExists);
	}

	private Task ApplyTrustGateAction(string pluginId,
		string version,
		PluginTrustGateAction action,
		PluginTrustResult onDisk)
	{
		return action == PluginTrustGateAction.None
			? Task.CompletedTask
			: UpsertTrustRecord(pluginId, version, onDisk.Verdict, onDisk.CertificateId);
	}

	// A record admitted as Trusted but refused by the gate (e.g. the certificate id no longer matches, or
	// the record has gone missing after the baseline) is not describable by the on-disk verdict alone -
	// SigningErrorTrustMapping only ever produces Trusted for a package that still verifies. Every other
	// refusal is a real crypto/revocation failure and maps normally.
	private static PluginInstallError ToActivationError(PluginTrustResult onDisk)
		=> onDisk.Verdict == PluginTrustVerdict.Trusted
			? PluginInstallError.SignatureUnverifiable
			: onDisk.Verdict.ToInstallError();

	private async Task<PluginInstallResult> InstallLocked(PluginManifest manifest,
		PluginArtifactAcquisition acquisition,
		PluginInstallRequest request,
		string stagingDirectory,
		CancellationToken cancellationToken)
	{
		PluginInstallerLog.Installing(_logger, manifest.Id, manifest.Version);

		var versionDirectory = PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory,
			manifest.Id,
			manifest.Version);

		if (Directory.Exists(versionDirectory) && !request.Force)
		{
			return PluginInstallResult.Fail(PluginInstallError.AlreadyInstalled,
					$"Plugin '{manifest.Id}' version {manifest.Version} is already installed.",
					manifest.Id,
					manifest.Version,
					ActiveVersionOf(manifest.Id))
				with
				{
					Publisher = manifest.Publisher
				};
		}

		if (acquisition.Sha256 is { } acquiredHash &&
			!string.Equals(ComputeSha256(acquisition.ArtifactPath!), acquiredHash, StringComparison.Ordinal))
		{
			return PluginInstallResult.Fail(PluginInstallError.HashMismatch,
				"The artifact changed on disk between validation and installation.",
				manifest.Id,
				manifest.Version);
		}

		var extractDirectory = Path.Combine(stagingDirectory, "extract");
		var extraction = await _artifactReader.ExtractTo(acquisition.ArtifactPath!,
			extractDirectory,
			cancellationToken);

		if (!extraction.Success)
		{
			return PluginInstallResult.Fail(extraction.Error ?? PluginInstallError.Failed,
				extraction.ErrorMessage ?? "The artifact could not be extracted.",
				manifest.Id,
				manifest.Version);
		}

		if (VerifyDigests(manifest, extraction.ExtractedFiles, extractDirectory) is { } digestFailure)
		{
			return PluginInstallResult.Fail(PluginInstallError.HashMismatch,
				digestFailure,
				manifest.Id,
				manifest.Version);
		}

		var previousVersion = ActiveVersionOf(manifest.Id);

		// Evaluated against the staged extracted tree, the same bytes VerifyDigests just walked and the
		// same form launch-time re-verification reads - never the archive, which nothing re-checks after
		// this point. Verifying the archive here and extracting from it earlier would leave a window
		// between the two reads for the staged archive to be swapped out from under the check.
		var trust = await _trustEvaluator.EvaluateInstalledAsync(extractDirectory, cancellationToken);

		// Keyed on the highest tier any installed version of this plugin id was ever admitted at, not just
		// the active version's record: current.json can go missing or unreadable, and previousVersion would
		// then be null even though other installed versions of this id were admitted as Trusted. Keying the
		// check on the active version alone would let an unsigned update through with consent in that case.
		var admittedVerdict = await GetHighestAdmittedTier(manifest.Id);

		if (PluginTrustPolicy.IsDowngrade(admittedVerdict, trust.Verdict))
		{
			return PluginInstallResult.Fail(PluginInstallError.TrustDowngrade,
					$"'{manifest.Id}' was previously admitted as trusted; this update verifies as " +
					$"{trust.Verdict} and would install at a lower trust tier.",
					manifest.Id,
					manifest.Version,
					previousVersion)
				with
				{
					Publisher = manifest.Publisher, Signature = trust
				};
		}

		// Written as an equality, never as a negation of "is trusted": widening this to admit any verdict
		// that merely isn't a hard failure would silently reopen the unsigned-consent path to a signature
		// that actively failed verification.
		var permitted = trust.Verdict == PluginTrustVerdict.Trusted ||
			(trust.Verdict == PluginTrustVerdict.Unsigned &&
				request.AllowUnsigned &&
				PluginTrustPolicy.PermitsUnsignedConsent(acquisition.SourceKind, await DeveloperModeEnabled()));

		if (!permitted)
		{
			return PluginInstallResult.Fail(trust.Verdict.ToInstallError(),
					trust.Message ?? $"The artifact's signature verifies as {trust.Verdict}.",
					manifest.Id,
					manifest.Version,
					previousVersion)
				with
				{
					Publisher = manifest.Publisher, Signature = trust
				};
		}

		MakeEntrypointsExecutable(manifest, extractDirectory);

		var displacedDirectory = Path.Combine(stagingDirectory, "displaced");

		if (PromoteStagedVersion(extractDirectory, versionDirectory, displacedDirectory, request.Force)
			is { } promoteFailure)
		{
			return PluginInstallResult.Fail(PluginInstallError.StagingFailed,
				promoteFailure,
				manifest.Id,
				manifest.Version,
				previousVersion);
		}

		var reread = _manifestReader.Read(
			PluginInstallPaths.ManifestPath(_paths.PluginsDirectory, manifest.Id, manifest.Version),
			manifest.Id,
			manifest.Version);

		if (!reread.Success)
		{
			RestoreDisplacedVersion(displacedDirectory, versionDirectory);
			return PluginInstallResult.Fail(PluginInstallError.ManifestInvalid,
				reread.ErrorMessage ?? "The installed manifest is not valid.",
				manifest.Id,
				manifest.Version,
				previousVersion);
		}

		var warnings = CollectWarnings(manifest, trust);
		var rollback = new PluginRollbackPlan(displacedDirectory, DeleteFailedVersion: true);

		// Before activation, not after: ActivateAndValidate starts the plugin, and the supervisor refuses
		// to launch an installed version that has no trust record. Recording it afterwards would make the
		// launch this method performs fail its own gate.
		await UpsertTrustRecord(manifest.Id, manifest.Version, trust.Verdict, trust.CertificateId);

		// Health-gating a plugin this machine cannot launch would fail every time and roll back a
		// perfectly good install of a cross-platform artifact.
		var canStart = request.StartAfterActivation && HasEntrypointForThisHost(manifest);
		var result = await ActivateAndValidate(manifest.Id,
			manifest.Version,
			warnings,
			canStart,
			rollback,
			cancellationToken);

		await FinishArtifact(acquisition, request, result.Success, cancellationToken);

		if (!result.Success)
		{
			await DeleteTrustRecord(manifest.Id, manifest.Version);
		}

		if (result.Success)
		{
			await PruneOldVersions(manifest.Id, previousVersion);

			// A plugin that was activated but deliberately not started never welcomes a session, and the
			// registrar only registers such plugins at startup. Without this an install held back by a
			// blocking warning, or one with no entrypoint for this machine, would have no integration at
			// all until the host restarts - invisible to the UI that installed it, and so unremovable.
			if (!canStart || result.HasBlockingWarning)
			{
				await _integrationRegistrar.RegisterInstalledDetachedAsync(manifest.Id, cancellationToken);
			}
		}

		// One attachment point covers every path out of ActivateAndValidate, rollback included. The
		// re-read manifest is deliberately not used: both come from the same manifest.json, and the
		// pre-promotion one is the copy every earlier decision in this method was made against.
		return result with
		{
			Publisher = manifest.Publisher,
			Signature = trust,
			SignedWith = manifest.Signature,
			Name = manifest.Name,
			Description = manifest.Description,
			SupportedOnThisPlatform = HasEntrypointForThisHost(manifest)
		};
	}

	private async Task<PluginInstallResult> ActivateAndValidate(string pluginId,
		string version,
		IReadOnlyList<PluginInstallWarning> warnings,
		bool startAfterActivation,
		PluginRollbackPlan rollback,
		CancellationToken cancellationToken)
	{
		var previousVersion = ActiveVersionOf(pluginId);
		var wasRunning = IsRunning(pluginId);

		await _supervisor.Stop(pluginId, PluginStopReason.Update, cancellationToken);

		try
		{
			WriteCurrentVersion(pluginId, version);
			_catalog.Invalidate();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			var undone = await RollBack(pluginId,
				version,
				previousVersion,
				wasRunning,
				rollback,
				cancellationToken);

			return PluginInstallResult.Fail(PluginInstallError.ActivationFailed,
				ex.Message,
				pluginId,
				version,
				previousVersion,
				undone,
				warnings);
		}

		PluginInstallerLog.Activated(_logger, pluginId, version, previousVersion ?? "nothing");

		try
		{
			await RevokeRegistrationRow(pluginId);
			await _sessionRegistry.TerminateForPlugin(pluginId,
				ProtocolCloseCodes.RegistrationRejected,
				"The plugin id is now claimed by an installed plugin.");
		}
		catch (Exception ex)
		{
			PluginInstallerLog.IdClaimFailed(_logger, pluginId, ex);
		}

		var blocked = warnings.Any(warning => warning.Severity == PluginInstallWarningSeverity.Blocking);
		if (!startAfterActivation || blocked)
		{
			return PluginInstallResult.Ok(pluginId, version, previousVersion, activated: true, warnings);
		}

		if (await ReachesHealthy(pluginId, cancellationToken))
		{
			return PluginInstallResult.Ok(pluginId, version, previousVersion, activated: true, warnings);
		}

		PluginInstallerLog.HealthValidationFailed(_logger, pluginId, version);
		var rolledBack = await RollBack(pluginId,
			version,
			previousVersion,
			wasRunning,
			rollback,
			cancellationToken);

		return PluginInstallResult.Fail(PluginInstallError.HealthValidationFailed,
			$"Version {version} did not become healthy.",
			pluginId,
			version,
			previousVersion,
			rolledBack,
			warnings);
	}

	private async Task<bool> RollBack(string pluginId,
		string failedVersion,
		string? previousVersion,
		bool restartPrevious,
		PluginRollbackPlan rollback,
		CancellationToken cancellationToken)
	{
		try
		{
			await _supervisor.Stop(pluginId, PluginStopReason.Update, cancellationToken);

			var failedDirectory = PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory,
				pluginId,
				failedVersion);

			if (rollback.DeleteFailedVersion)
			{
				TryDeleteDirectoryWithRetries(failedDirectory);
			}

			RestoreDisplacedVersion(rollback.DisplacedDirectory, failedDirectory);

			var previousDirectory = previousVersion is null
				? null
				: PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, pluginId, previousVersion);

			if (previousVersion is not null && Directory.Exists(previousDirectory))
			{
				WriteCurrentVersion(pluginId, previousVersion);
			}
			else
			{
				_currentVersionFiles.Delete(PluginInstallPaths.CurrentFilePath(_paths.PluginsDirectory, pluginId));
			}

			_catalog.Invalidate();

			if (restartPrevious &&
				previousVersion is not null &&
				Directory.Exists(previousDirectory))
			{
				await _supervisor.Start(pluginId, cancellationToken);
			}

			PluginInstallerLog.RolledBack(_logger, pluginId, previousVersion ?? "no active version");
			return true;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			PluginInstallerLog.RollbackFailed(_logger, pluginId, ex);
			return false;
		}
	}

	private readonly record struct PluginRollbackPlan(string? DisplacedDirectory, bool DeleteFailedVersion)
	{
		public static PluginRollbackPlan None => new(null, DeleteFailedVersion: false);
	}

	private void RestoreDisplacedVersion(string? displacedDirectory, string versionDirectory)
	{
		if (displacedDirectory is null || !Directory.Exists(displacedDirectory))
		{
			return;
		}

		try
		{
			TryDeleteDirectoryWithRetries(versionDirectory);
			Directory.Move(displacedDirectory, versionDirectory);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			PluginInstallerLog.RollbackFailed(_logger, versionDirectory, ex);
		}
	}

	private async Task<bool> ReachesHealthy(string pluginId, CancellationToken cancellationToken)
	{
		var start = await _supervisor.Start(pluginId, cancellationToken);
		if (!start.Success)
		{
			return false;
		}

		var deadline = _timeProvider.GetUtcNow() + _options.ActivationHealthTimeout;
		while (_timeProvider.GetUtcNow() < deadline)
		{
			var snapshot = _supervisor.Snapshot()
				.FirstOrDefault(entry => string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal));

			if (snapshot is not null)
			{
				if (snapshot.State is PluginRuntimeState.Failed)
				{
					return false;
				}

				if (snapshot.State == PluginRuntimeState.Running &&
					snapshot.Health is PluginHealthState.Healthy or PluginHealthState.Degraded)
				{
					return true;
				}
			}

			await Task.Delay(_healthPollInterval, _timeProvider, cancellationToken);
		}

		return false;
	}

	private string? Incompatibility(PluginManifest manifest)
	{
		if (manifest.Compatibility is not { } compatibility)
		{
			return null;
		}

		if (compatibility.Protocol is { } protocol &&
			(protocol.Minimum > ProtocolVersions.Current || protocol.Maximum < ProtocolVersions.Minimum))
		{
			return $"The plugin needs plugin protocol {protocol.Minimum}-{protocol.Maximum}; this host " +
				$"speaks {ProtocolVersions.Minimum}-{ProtocolVersions.Current}.";
		}

		if (compatibility.MacroDeck is not { } hostRange || string.IsNullOrWhiteSpace(hostRange))
		{
			return null;
		}

		if (string.Equals(HostVersion.Current, HostVersion.FallbackVersion, StringComparison.Ordinal))
		{
			PluginInstallerLog.CompatibilityCheckSkipped(_logger, manifest.Id);
			return null;
		}

		if (SemanticVersionRange.TryParse(hostRange, out var range) &&
			SemanticVersion.TryParse(HostVersion.Current, out var current) &&
			range.Satisfies(current))
		{
			return null;
		}

		return $"The plugin needs Macro Deck '{hostRange}'; this host is {HostVersion.Current}.";
	}

	private List<PluginInstallWarning> CollectWarnings(PluginManifest manifest,
		PluginTrustResult? trust)
	{
		var warnings = new List<PluginInstallWarning>(_dependencyResolver.Resolve(manifest));

		if (manifest.Files is null || manifest.Files.Count == 0)
		{
			warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.NoFileDigests,
				"The artifact declares no file digests, so its contents could not be verified."));
		}

		if (manifest.Compatibility?.Sdk is { } sdkRange && !string.IsNullOrWhiteSpace(sdkRange))
		{
			warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.SdkCompatibilityUnchecked,
				$"The plugin declares SDK compatibility '{sdkRange}', which is recorded but not checked " +
				"until the plugin connects."));
		}

		if (manifest.Permissions is { Count: > 0 } permissions)
		{
			warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.PermissionsRequested,
				$"The plugin declares the permissions: {string.Join(", ", permissions)}."));
		}

		if (!HasEntrypointForThisHost(manifest))
		{
			// Advisory, not blocking: a cross-platform artifact that simply was not built for this machine
			// is perfectly valid and worth keeping installed. It just cannot be started here, which the
			// caller handles separately - see how StartAfterActivation is computed.
			warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.NoEntrypointForRuntime,
				$"The artifact ships no entrypoint for {PluginRuntimeIdentifiers.Current}, so it cannot " +
				"run on this machine."));
		}

		switch (trust?.Verdict)
		{
			case PluginTrustVerdict.Unsigned:
				warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.ArtifactUnsigned,
					"The artifact is not signed."));
				break;
			case PluginTrustVerdict.VerificationUnavailable:
			case PluginTrustVerdict.RevocationUnavailable:
				warnings.Add(PluginInstallWarning.Advisory(PluginDependencyWarningCodes.SignatureUnverified,
					trust.Message ?? "The artifact signature could not be verified by this host."));
				break;
			default:
				break;
		}

		return warnings;
	}

	private static bool HasEntrypointForThisHost(PluginManifest manifest)
	{
		var candidates = PluginRuntimeIdentifiers.CandidatesFor(PluginRuntimeIdentifiers.Current);
		return candidates.Any(manifest.Entrypoints.ContainsKey);
	}

	private static string? VerifyDigests(PluginManifest manifest,
		IReadOnlyList<string> extractedFiles,
		string extractDirectory)
	{
		if (manifest.Files is not { Count: > 0 } declared)
		{
			return null;
		}

		var declaredByPath = declared.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
		var signed = manifest.Signature is not null;

		foreach (var relativePath in extractedFiles)
		{
			if (IsRootSignatureMaterial(relativePath, signed))
			{
				continue;
			}

			if (!declaredByPath.ContainsKey(relativePath))
			{
				return $"'{relativePath}' is present in the artifact but not declared in its file list.";
			}
		}

		foreach (var file in declared)
		{
			var absolutePath = Path.Combine(extractDirectory,
				file.Path.Replace('/', Path.DirectorySeparatorChar));

			if (!File.Exists(absolutePath))
			{
				return $"'{file.Path}' is declared in the file list but missing from the artifact.";
			}

			var info = new FileInfo(absolutePath);
			if (info.Length != file.Size)
			{
				return $"'{file.Path}' is {info.Length} bytes; the manifest declares {file.Size}.";
			}

			if (!string.Equals(ComputeSha256(absolutePath), file.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				return $"'{file.Path}' does not match its declared digest.";
			}
		}

		return null;
	}

	/// <summary>True for <c>manifest.json</c> itself, always excluded from <c>files[]</c>, and - only when
	/// <paramref name="signed"/> - for the certificate and its root signature a signed plugin carries
	/// alongside it. An unsigned artifact has no certificate to exclude, so it must declare those two
	/// paths like any other file rather than smuggle them past the check.</summary>
	private static bool IsRootSignatureMaterial(string relativePath, bool signed)
		=> string.Equals(relativePath, PluginArtifactFiles.ManifestFileName, StringComparison.OrdinalIgnoreCase) ||
			(signed &&
				(string.Equals(relativePath,
						PluginArtifactFiles.CertificateFileName,
						StringComparison.OrdinalIgnoreCase) ||
					string.Equals(relativePath,
						PluginArtifactFiles.CertificateSignatureFileName,
						StringComparison.OrdinalIgnoreCase)));

	private static string ComputeSha256(string path)
	{
		using var stream = File.OpenRead(path);
		return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(stream));
	}

	private static void MakeEntrypointsExecutable(PluginManifest manifest, string extractDirectory)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		const UnixFileMode executableMode = UnixFileMode.UserRead |
			UnixFileMode.UserWrite |
			UnixFileMode.UserExecute |
			UnixFileMode.GroupRead |
			UnixFileMode.GroupExecute |
			UnixFileMode.OtherRead |
			UnixFileMode.OtherExecute;

		foreach (var rid in PluginRuntimeIdentifiers.CandidatesFor(PluginRuntimeIdentifiers.Current))
		{
			if (!manifest.Entrypoints.TryGetValue(rid, out var entrypoint))
			{
				continue;
			}

			var path = Path.Combine(extractDirectory,
				entrypoint.Executable.Replace('/', Path.DirectorySeparatorChar));

			if (!File.Exists(path))
			{
				continue;
			}

			try
			{
				File.SetUnixFileMode(path, executableMode);
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
			}
		}
	}

	private string? PromoteStagedVersion(string extractDirectory,
		string versionDirectory,
		string displacedDirectory,
		bool force)
	{
		var moved = false;

		try
		{
			var parent = Path.GetDirectoryName(versionDirectory);
			if (parent is not null)
			{
				Directory.CreateDirectory(parent);
			}

			if (Directory.Exists(versionDirectory))
			{
				if (!force)
				{
					return $"'{versionDirectory}' already exists.";
				}

				Directory.Move(versionDirectory, displacedDirectory);
				moved = true;
			}

			Directory.Move(extractDirectory, versionDirectory);
			return null;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			if (moved)
			{
				RestoreDisplacedVersion(displacedDirectory, versionDirectory);
			}

			return ex.Message;
		}
	}

	private async Task FinishArtifact(PluginArtifactAcquisition acquisition,
		PluginInstallRequest request,
		bool installSucceeded,
		CancellationToken cancellationToken)
	{
		if (!acquisition.HostOwned || acquisition.ArtifactPath is not { } artifactPath)
		{
			return;
		}

		// A local-path source names a file the caller already has on disk; caching a second copy of it
		// under its digest would only ever save a re-download that source can never need.
		if (acquisition.SourceKind == PluginArtifactSourceKind.LocalPath)
		{
			return;
		}

		var retain = installSucceeded && (request.RetainDownload || _options.RetainDownloads);
		if (!retain || acquisition.Sha256 is not { } sha256)
		{
			return;
		}

		try
		{
			await _cache.Put(artifactPath, sha256, cancellationToken);
			_cache.Prune();
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			PluginInstallerLog.StagingCleanupFailed(_logger, artifactPath, ex);
		}
	}

	private async Task PruneOldVersions(string pluginId, string? rollbackTarget)
	{
		var active = ActiveVersionOf(pluginId);
		var versionsDirectory = PluginInstallPaths.VersionsDirectory(_paths.PluginsDirectory, pluginId);

		if (!Directory.Exists(versionsDirectory))
		{
			return;
		}

		var candidates = Directory.EnumerateDirectories(versionsDirectory)
			.Select(Path.GetFileName)
			.OfType<string>()
			.Where(version => !string.Equals(version, active, StringComparison.Ordinal) &&
				!string.Equals(version, rollbackTarget, StringComparison.Ordinal))
			.OrderByDescending(version =>
				SemanticVersion.TryParse(version, out var parsed) ? parsed : null)
			.ThenByDescending(version => version, StringComparer.Ordinal)
			.ToList();

		var keepBesidesActive = Math.Max(0, _options.VersionsToRetain - 1);
		foreach (var version in candidates.Skip(keepBesidesActive))
		{
			if (TryDeleteDirectoryWithRetries(
				PluginInstallPaths.VersionDirectory(_paths.PluginsDirectory, pluginId, version)))
			{
				PluginInstallerLog.PrunedVersion(_logger, pluginId, version);
				await DeleteTrustRecordVersion(pluginId, version);
			}
		}

		_catalog.Invalidate();
	}

	private bool TryResolveInstalledVersion(string pluginId, string version, out PluginManifest manifest)
	{
		manifest = null!;

		if (!PluginId.IsValid(pluginId))
		{
			return false;
		}

		var manifestPath = PluginInstallPaths.ManifestPath(_paths.PluginsDirectory, pluginId, version);
		if (!File.Exists(manifestPath))
		{
			return false;
		}

		var read = _manifestReader.Read(manifestPath, pluginId, version);
		if (!read.Success || read.Manifest is null)
		{
			return false;
		}

		manifest = read.Manifest;
		return true;
	}

	private string? ActiveVersionOf(string pluginId)
	{
		return _catalog.Discover()
			.FirstOrDefault(plugin => string.Equals(plugin.PluginId, pluginId, StringComparison.Ordinal))
			?.ActiveVersion?.Version;
	}

	private bool IsRunning(string pluginId)
	{
		var snapshot = _supervisor.Snapshot()
			.FirstOrDefault(entry => string.Equals(entry.PluginId, pluginId, StringComparison.Ordinal));

		return snapshot?.State is PluginRuntimeState.Running or PluginRuntimeState.Starting;
	}

	private void WriteCurrentVersion(string pluginId, string version)
	{
		var path = PluginInstallPaths.CurrentFilePath(_paths.PluginsDirectory, pluginId);
		var directory = Path.GetDirectoryName(path);
		if (directory is not null)
		{
			Directory.CreateDirectory(directory);
		}

		_currentVersionFiles.Write(path, new CurrentVersionDocument(version));
	}

	private string CreateStagingDirectory()
	{
		var path = Path.Combine(_paths.PluginStagingDirectory, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(path);
		return path;
	}

	private void DeleteDirectory(string path)
	{
		if (!TryDeleteDirectoryWithRetries(path) && Directory.Exists(path))
		{
			PluginInstallerLog.StagingCleanupFailed(_logger,
				path,
				new IOException($"'{path}' could not be removed."));
		}
	}

	private bool TryDeleteDirectoryWithRetries(string path)
	{
		if (!Directory.Exists(path))
		{
			return true;
		}

		for (var attempt = 1; attempt <= DeleteAttempts; attempt++)
		{
			try
			{
				Directory.Delete(path, recursive: true);
				return true;
			}
			catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
			{
				if (attempt == DeleteAttempts)
				{
					PluginInstallerLog.DeleteFailed(_logger, path, DeleteAttempts, ex);
					return false;
				}

				Thread.Sleep(_deleteRetryDelay);
			}
		}

		return false;
	}

	private sealed record CurrentVersionDocument(string Version);
}
