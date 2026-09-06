using System.Globalization;
using System.Security.Cryptography;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Network.Tls;
using MacroDeckHost.Extensions;
using MacroDeckHost.Infrastructure.Network;
using MacroDeckHost.Infrastructure.Network.Tls;
using MacroDeckHost.Infrastructure.Backups.Restore;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Security.KeyRing;
using MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;
using MacroDeckHost.Integrations.Native;
using MacroDeckHost.Logging;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace MacroDeckHost;

public static class Program
{
	public static void Main(string[] args)
	{
		if (!OperatingSystem.IsMacOS())
		{
			RunHostAsync(args).GetAwaiter().GetResult();
			return;
		}

		// Claimed before the host task starts so the watcher factory cannot observe it unset and
		// silently pick the polling fallback for the process lifetime.
		MacOsMainRunLoop.MarkPumping();
		var host = Task.Run(() => RunHostAsync(args));
		try
		{
			MacOsMainRunLoop.PumpUntil(host);
		}
		catch (Exception e)
		{
			Log.Error(e, "The macOS main run loop stopped early");
		}

		host.GetAwaiter().GetResult();
	}

	private static async Task RunHostAsync(string[] args)
	{
		ProcessDpiAwareness.Configure();

		var paths = new MacroDeckPaths();

		var logLevelState = new LogLevelState(LogLevelDefaults.ForChannel(BuildConfig.Channel));

		Log.Logger = new LoggerConfiguration()
			.MinimumLevel.ControlledBy(logLevelState.Global)
			.Enrich.With(new LogOriginEnricher())
			.WriteTo.Redacted(sinks =>
			{
				sinks.Console(formatProvider: CultureInfo.InvariantCulture);
				sinks.File(RedactingTextFormatter.ForFileSink(),
					Path.Combine(paths.LogsDirectory, "host-.log"),
					rollingInterval: RollingInterval.Day,
					retainedFileCountLimit: 14,
					shared: true);
			})
			.CreateLogger();

		AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledException;

		TaskScheduler.UnobservedTaskException += (_, e) =>
		{
			Log.Error(e.Exception.Flatten(), "Unobserved task exception");
			e.SetObserved();
		};

		try
		{
			var loopbackPort = HostEndpoints.ResolveLoopbackPort();

			if (await SingleInstanceGuard.IsAnotherInstanceRunning())
			{
				Log.Information("Another host instance is already running, exiting");
				return;
			}

			// The only safe point to swap the data root: nothing has opened the database, the Data Protection
			// key ring, the TLS private key or a plugin binary yet. The applier takes its own exclusive lock
			// because the single-instance probe above is a best-effort HTTP check, not a lock.
			var restoreOutcome = StagedRestoreApplier.ApplyPending(paths);

			HostStartupFacts.RestoreApplied = restoreOutcome == StagedRestoreOutcome.Applied;

			DatabaseMigrationHelper.MigrateDatabase(paths);

			var preferences = EarlyAppPreferenceReader.TryReadMany(paths,
				AppPreferenceService.PublicPortKey,
				AppPreferenceService.TlsEnabledKey,
				AppPreferenceService.TlsModeKey,
				AppPreferenceService.TlsHttpsPortKey,
				AppPreferenceService.BackupRecoveryKeyExportedAtKey,
				AppPreferenceService.InstallationIdKey);
			var configuredPort = preferences.GetValueOrDefault(AppPreferenceService.PublicPortKey);
			var selection = PublicPortSelector.Resolve(
				Environment.GetEnvironmentVariable(BuildConfig.PublicPortEnvironmentVariable),
				configuredPort,
				loopbackPort);
			var refusedPort = selection.Source == PublicPortSource.Default &&
				int.TryParse(configuredPort, out var parsedConfiguredPort)
					? parsedConfiguredPort
					: (int?)null;
			if (!string.IsNullOrWhiteSpace(configuredPort) && selection.Source != PublicPortSource.Preference)
			{
				Log.Warning("Ignoring the configured public port {ConfiguredPort}; using {Port} ({Source}) instead",
					configuredPort,
					selection.Port,
					selection.Source);
			}

			var publicPort = selection.Port;

			if (publicPort == loopbackPort)
			{
				throw new InvalidOperationException("The public and loopback ports must be different.");
			}

			// Known before Kestrel binds whenever the bootstrapper assigned it. A host started by hand
			// in Production lets the OS choose, and LoopbackPortFileService publishes it once bound;
			// until then the auth trust boundary fails closed rather than guessing which port is which.
			if (loopbackPort > 0)
			{
				ResolvedLoopbackPort.Set(loopbackPort);
			}

			// How the key ring is protected has to be settled before the provider is built, and the
			// provider is built before the container exists. Nothing here decrypts or writes.
			var keyRingPlan = ResolveKeyRingProtection(paths, preferences);

			// One provider for the whole process, built here because the TLS private key has to be
			// unprotected before Kestrel is configured. Registered into DI below so AddDataProtection's
			// TryAdd leaves it in place: two providers over one key ring would be a standing invitation
			// for their application names to drift apart and stop unprotecting each other's payloads.
			// Startup.cs configures the second registration through the same KeyRingDataProtection call.
			var dataProtectionProvider = DataProtectionProvider.Create(
				new DirectoryInfo(KeyRingStartupState.ScratchKeysDirectory ?? paths.KeysDirectory),
				builder =>
				{
					builder.SetApplicationName("MacroDeck");
					KeyRingDataProtection.Configure(builder, KeyRingStartupState.KekHolder, keyRingPlan.Mode);
				});
			var certificateStore = new FilePublicTlsCertificateStore(dataProtectionProvider,
				paths,
				Log.ForContext<FilePublicTlsCertificateStore>());

			// A certificate always exists, seeded on the first start that finds none, and from here on it
			// is issued by a per-installation local certificate authority so it can be renewed - after a
			// DHCP change, or before it expires - without any device having to trust anything again. The
			// authority itself and any certificate the user uploaded are never replaced silently.
			//
			// Runs before the endpoints are resolved and before the certificate is loaded, so the
			// fingerprint the listener serves is the one the settings screen reports and a migrating
			// upgrade does not raise a restart prompt it can never clear.
			//
			// Skipped while the key ring is locked: the provider is pointed at a scratch ring then, so
			// seeding would write a private key that nothing can read once the real ring is back.
			new PublicTlsBootstrapper(certificateStore,
					new LocalAddressProvider(),
					new HostNameProvider(),
					Log.ForContext<PublicTlsBootstrapper>())
				.Run(keyRingPlan.Mode == KeyRingProtectionMode.Locked, DateTimeOffset.UtcNow);

			// The HTTPS port has an environment override for the same reason the public port does: HTTPS now
			// always binds, so two checkouts or two CI jobs on one machine would otherwise contend for the
			// single default port.
			var configuredHttpsPort =
				Environment.GetEnvironmentVariable(BuildConfig.PublicHttpsPortEnvironmentVariable) ??
				preferences.GetValueOrDefault(AppPreferenceService.TlsHttpsPortKey);
			var tls = PublicTlsSelector.Resolve(preferences.GetValueOrDefault(AppPreferenceService.TlsEnabledKey),
				preferences.GetValueOrDefault(AppPreferenceService.TlsModeKey),
				configuredHttpsPort,
				certificateStore.ReadInfo() is not null,
				publicPort,
				loopbackPort);
			if (tls.Rejection != PublicTlsRejection.None)
			{
				Log.Warning("The stored HTTPS configuration was not applied ({Rejection}); " +
					"review it in Settings > Network",
					tls.Rejection);
			}

			// Loaded eagerly rather than through a Kestrel certificate-selection callback: the listener
			// plan has to be decided before anything binds, so a certificate that cannot be loaded
			// costs the HTTPS listener instead of binding a port that then fails every handshake.
			var certificate = tls.Endpoints.HttpsPort is null
				? new PublicTlsCertificateResolution(null, PublicTlsFailure.None)
				: certificateStore.LoadServerCertificate();

			// Probes each public port before Kestrel is configured, so a port the OS refuses to bind
			// (issue #515) costs only that listener instead of the whole host.
			var listenerPlan = HostListenerPlan.Create(tls.Endpoints,
				loopbackPort,
				certificate.Certificate);
			ResolvedPublicEndpoints.Set(listenerPlan.Endpoints);
			var activeFingerprint = listenerPlan.Endpoints.HttpsPort is null
				? null
				: certificate.Certificate?.GetCertHashString(HashAlgorithmName.SHA256);

			var listenerState = new HostListenerState(listenerPlan.Endpoints,
				selection.Source == PublicPortSource.Environment,
				refusedPort,
				certificate.Failure,
				tls.Rejection,
				activeFingerprint);

			foreach (var endpoint in listenerPlan.Endpoints.Endpoints)
			{
				Log.Information("Public listener on {Scheme}://0.0.0.0:{Port} (source {Source})",
					endpoint.Ssl ? "https" : "http",
					endpoint.Port,
					selection.Source);
			}

			var host = Host.CreateDefaultBuilder(args)
				.ConfigureSerilog(paths, logLevelState)
				.ConfigureServices(services =>
				{
					services.AddSingleton<ILogLevelState>(logLevelState);
					services.AddSingleton<IHostListenerState>(listenerState);
					services.AddSingleton(listenerPlan.CertificateHolder);
					services.AddSingleton(dataProtectionProvider);
					services.AddSingleton(keyRingPlan);
					services.AddSingleton(KeyRingStartupState.KekHolder);
					services.AddSingleton(KeyRingStartupState.Store);
					services.AddSingleton(KeyRingStartupState.Identity);
				})
				.ConfigureWebHostDefaults(hostBuilder =>
				{
					hostBuilder.UseStartup<Startup>();
					hostBuilder.ConfigureKestrel(listenerPlan.Apply);
				})
				.Build();

			await SeedLogLevelAsync(host, logLevelState);

			// The shell tells a requested restart from a crash by the exit code alone, so it has to
			// survive the graceful shutdown RunAsync performs.
			var exitCode = await HostRunner.RunAsync(host);
			if (exitCode == HostExitCodes.RestartRequested)
			{
				Log.Information("Restart requested; exiting with code {ExitCode} so the desktop app relaunches",
					exitCode);
				Environment.ExitCode = exitCode;
			}
		}
		catch (Exception e)
		{
			Log.Fatal(e,
				"Host terminated unexpectedly (public port {PublicPort})",
				ResolvedPublicEndpoints.Value.PublicPort);
			Environment.ExitCode = 1;
		}
		finally
		{
			await Log.CloseAndFlushAsync();
		}
	}

	private static KeyRingProtectionPlan ResolveKeyRingProtection(MacroDeckPaths paths,
		IReadOnlyDictionary<string, string> preferences)
	{
		var store = KekStoreFactory.Create();
		var portable = MacroDeckDataRootResolver.IsPortable(DataRootEnvironment.Current());

		// The account is keyed on the installation id so two checkouts, or a portable install beside a
		// packaged one, cannot write over each other's entry. It is seeded here rather than left to the
		// hosted service that normally creates it: on a first run that service has not run yet, so a
		// recovery key exported in that same session would store the key under a placeholder account and
		// the next start would not find it.
		var installationId = Guid.TryParse(EarlyAppPreferenceReader.EnsureValue(paths,
				AppPreferenceService.InstallationIdKey,
				Guid.NewGuid().ToString("D")),
			out var parsed)
			? parsed
			: Guid.Empty;
		var identity = KekStoreIdentity.For(BuildConfig.ApplicationDisplayName, installationId);

		// Regenerate writes an empty string here, so presence alone would read a regenerated but never
		// exported key as exported and wrap the ring too early.
		var exported = DateTimeOffset.TryParse(
			preferences.GetValueOrDefault(AppPreferenceService.BackupRecoveryKeyExportedAtKey),
			CultureInfo.InvariantCulture,
			DateTimeStyles.RoundtripKind,
			out _);

		var plan = KeyRingProtectionPlanner.Resolve(paths.KeysDirectory, store, identity, exported, portable);

		string? scratchKeys = null;
		if (plan.Mode == KeyRingProtectionMode.Locked)
		{
			// Outside the data root on purpose: it must never be backed up, restored, or mistaken for
			// the real ring.
			scratchKeys = Path.Combine(Path.GetTempPath(), "macro-deck-locked-keys", Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(scratchKeys);

			Log.Error("The key ring is locked ({Reason}). Stored secrets stay untouched until it is unlocked",
				plan.LockReason);
		}
		else if (plan.Mode == KeyRingProtectionMode.Unprotected &&
			plan.UnprotectedReason != KeyRingUnprotectedReason.None)
		{
			Log.Information("The key ring is not protected by the operating system keystore ({Reason})",
				plan.UnprotectedReason);
		}

		if (plan.Kek is not null)
		{
			KeyRingStartupState.KekHolder.Set(plan.Kek);
		}

		KeyRingStartupState.Set(plan, KeyRingStartupState.KekHolder, store, identity, portable, scratchKeys);

		return plan;
	}

	private static void CurrentDomainOnUnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		Log.Logger.Fatal(e.ExceptionObject as Exception,
			"Unhandled exception {Terminating}",
			e.IsTerminating ? "Terminating" : "Not terminating");
	}

	// Apply the persisted minimum log level to the live pipeline before the host serves traffic.
	private static async Task SeedLogLevelAsync(IHost host, LogLevelState logLevelState)
	{
		try
		{
			await using var scope = host.Services.CreateAsyncScope();
			var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();
			logLevelState.Minimum = (await preferences.GetLogging()).MinimumLevel;
		}
		catch (Exception e)
		{
			Log.Warning(e, "Could not seed the minimum log level; keeping the build default");
		}
	}
}
