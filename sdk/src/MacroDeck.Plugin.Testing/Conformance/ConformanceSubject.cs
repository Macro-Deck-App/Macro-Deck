using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace MacroDeck.Plugin.Testing.Conformance;

/// <summary>
/// A recipe for starting the plugin under conformance test - in process, as an already-built executable,
/// or unpacked from a <c>.macroDeckPlugin</c> artifact - rather than a single running instance.
///
/// <para>
/// A recipe on purpose: several checks (version negotiation against an incompatible host, a resume window
/// short enough to force a fresh session, readiness observed before any session can exist) need a plugin
/// instance started against a host configured differently from the one every other check shares. Such a
/// check calls <see cref="StartAsync" /> again, against a host it built itself, instead of disturbing
/// <see cref="ConformanceContext.Host" />.
/// </para>
/// </summary>
public abstract class ConformanceSubject : IAsyncDisposable
{
	protected private ConformanceSubject()
	{
	}

	/// <summary>
	/// A subject built and run in this process from <paramref name="configure" /> - the same delegate a
	/// plugin author already passes to <c>MacroDeckPlugin.CreatePlugin()</c>. Every instance
	/// <see cref="StartAsync" /> produces is hosted with <see cref="PluginTestCredentials.SelfRegistering" />
	/// and given a fresh <see cref="ManualTimeProvider" /> as its <see cref="TimeProvider" /> - the only
	/// subject kind <see cref="ConformanceSubjectHandle.Clock" /> is ever set for.
	/// </summary>
	/// <param name="configure">Configures the plugin under test.</param>
	/// <param name="manifest">
	/// The plugin's identity - a fresh <see cref="PluginTestManifest" /> when omitted. Owned by the
	/// returned subject either way (given or defaulted) and disposed with it, so every
	/// <see cref="StartAsync" /> call against the same subject keeps the same identity across restarts,
	/// the way a <c>configure</c> delegate that closed over a fixed id already did before manifests
	/// existed. Pass one explicitly to give the subject a specific, known identity -
	/// <see cref="ConformanceSubjectHandle.Manifest" /> is always this same manifest's content, projected
	/// onto <c>MacroDeck.Plugin.Packaging.Manifest.PluginManifest</c>.
	/// </param>
	public static ConformanceSubject InProcess(Action<PluginHostBuilder> configure, PluginTestManifest? manifest = null)
	{
		ArgumentNullException.ThrowIfNull(configure);
		return new InProcessConformanceSubject(configure, manifest);
	}

	/// <summary>
	/// A subject launched as a real, separate process from an already-built executable. Every instance
	/// <see cref="StartAsync" /> produces is launched with <see cref="PluginTestCredentials.Managed" /> - see
	/// <see cref="MacroDeckTestHost.LaunchAsync" />.
	/// </summary>
	public static ConformanceSubject Executable(PluginLaunchSpec spec)
	{
		ArgumentNullException.ThrowIfNull(spec);
		return new ExecutableConformanceSubject(spec);
	}

	/// <summary>
	/// A subject extracted from a packed <c>.macroDeckPlugin</c> artifact and launched as a real process.
	/// The artifact is inspected and extracted once, on the first call to <see cref="StartAsync" />; every
	/// later call launches a fresh process from that same extracted copy.
	/// </summary>
	public static ConformanceSubject Artifact(string macroDeckPluginPath)
	{
		ArgumentException.ThrowIfNullOrEmpty(macroDeckPluginPath);
		return new ArtifactConformanceSubject(macroDeckPluginPath);
	}

	/// <summary>
	/// Starts one fresh instance of this subject against <paramref name="host" />. Does not wait for a
	/// session to complete its handshake - call <c>host.WaitForSessionAsync()</c> for that, or leave
	/// <paramref name="host" />'s <see cref="MacroDeckTestHostOptions.SessionCreation" /> held to observe the
	/// instance before a session can exist at all. The caller owns disposing the returned
	/// <see cref="ConformanceSubjectHandle.Plugin" />; disposing <paramref name="host" /> is the caller's
	/// responsibility too when it built <paramref name="host" /> itself.
	/// </summary>
	/// <param name="host">The host to start this instance against.</param>
	/// <param name="stateDirectory">
	/// Where a self-registering instance persists its credentials. Honoured only by an
	/// <see cref="InProcess" /> subject - passing the same directory across two calls is what lets a check
	/// prove self-registration persists across a restart, the way <c>MacroDeckTestHost.HostAsync</c>'s own
	/// remarks describe. Ignored by <see cref="Executable" /> and <see cref="Artifact" />, which always
	/// launch with <see cref="PluginTestCredentials.Managed" /> and so never persist anything to begin with.
	/// </param>
	/// <param name="cancellationToken">Cancels starting the instance.</param>
	public Task<ConformanceSubjectHandle> StartAsync(
		MacroDeckTestHost host,
		string? stateDirectory = null,
		CancellationToken cancellationToken = default)
		=> StartAsync(host, stateDirectory, credentials: null, cancellationToken);

	/// <summary>
	/// Starts one fresh instance of this subject against <paramref name="host" />, authenticating with
	/// <paramref name="credentials" />. See the <see cref="StartAsync(MacroDeckTestHost, string, CancellationToken)" />
	/// overload for what starting an instance does and who owns disposing it.
	/// </summary>
	/// <param name="host">The host to start this instance against.</param>
	/// <param name="stateDirectory">
	/// Where a self-registering instance persists its credentials. Honoured only by an
	/// <see cref="InProcess" /> subject - passing the same directory across two calls is what lets a check
	/// prove self-registration persists across a restart, the way <c>MacroDeckTestHost.HostAsync</c>'s own
	/// remarks describe. Ignored by <see cref="Executable" /> and <see cref="Artifact" />, which always
	/// launch with <see cref="PluginTestCredentials.Managed" /> and so never persist anything to begin with.
	/// </param>
	/// <param name="credentials">
	/// How the instance should authenticate. Honoured only by an <see cref="InProcess" /> subject, which
	/// defaults to <see cref="PluginTestCredentials.SelfRegistering" /> when null - pass
	/// <see cref="PluginTestCredentials.Pairing" /> for a check that exercises interactive pairing instead.
	/// Ignored by <see cref="Executable" /> and <see cref="Artifact" />, which always launch with
	/// <see cref="PluginTestCredentials.Managed" />.
	/// </param>
	/// <param name="cancellationToken">Cancels starting the instance.</param>
	public abstract Task<ConformanceSubjectHandle> StartAsync(
		MacroDeckTestHost host,
		string? stateDirectory,
		PluginTestCredentials? credentials,
		CancellationToken cancellationToken = default);

	/// <summary>Releases whatever this recipe itself owns (e.g. an artifact's extracted temporary directory) -
	/// never a running instance <see cref="StartAsync" /> produced, which its caller disposes separately.</summary>
	public abstract ValueTask DisposeAsync();

	private sealed class InProcessConformanceSubject(Action<PluginHostBuilder> configure, PluginTestManifest? manifest)
		: ConformanceSubject
	{
		// Defaulted once, here, rather than per-StartAsync call: a subject started more than once (see
		// this type's own remarks) must report the same identity every time.
		private readonly PluginTestManifest _manifest = manifest ?? new PluginTestManifest();

		public override async Task<ConformanceSubjectHandle> StartAsync(
			MacroDeckTestHost host,
			string? stateDirectory = null,
			PluginTestCredentials? credentials = null,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(host);

			var builder = MacroDeckPlugin.CreatePlugin();
			configure(builder);

			if (stateDirectory is { Length: > 0 })
			{
				builder.Configuration[$"{PluginHostOptions.SectionName}:StateDirectory"] = stateDirectory;
			}

			var clock = new ManualTimeProvider();
			builder.ConfigureServices((_, services) => services.AddSingleton<TimeProvider>(clock));

			var plugin = await host.HostAsync(builder, credentials ?? PluginTestCredentials.SelfRegistering, _manifest)
				.ConfigureAwait(false);

			// Projected onto the packaging package's own manifest DTO - the one every check in
			// ManifestAndIdentifierChecks.cs already reads - rather than exposing PluginTestManifest
			// itself through ConformanceSubjectHandle.Manifest, which stays the artifact/in-process common
			// shape. Entrypoints is always empty: an in-process subject is never launched from one, and
			// nothing in this suite's checks inspects it for a non-artifact subject (see this file's own
			// remarks on keeping honest skips honest).
			var pluginManifest = new PluginManifest
			{
				ManifestVersion = PluginManifest.SupportedManifestVersion,
				Id = _manifest.Id,
				Name = _manifest.Name,
				Version = _manifest.Version,
				Description = _manifest.Description,
				Icon = _manifest.Icon,
				Entrypoints = new Dictionary<string, PluginEntrypoint>()
			};

			return new ConformanceSubjectHandle { Plugin = plugin, Clock = clock, Manifest = pluginManifest };
		}

		/// <summary>
		/// Always disposes <see cref="_manifest" />, whether this subject generated it or a caller passed
		/// it in - unlike <see cref="PluginTestHarness" />/<see cref="MacroDeckTestHost.HostAsync" />'s
		/// one-shot calls, this subject is a reusable recipe (<see cref="StartAsync" /> may run more than
		/// once), so a caller-supplied manifest's lifetime naturally matches the subject's, not some
		/// separately managed scope; nothing about the manifest is needed once the subject that was
		/// started from it is done.
		/// </summary>
		public override ValueTask DisposeAsync()
		{
			_manifest.Dispose();
			return ValueTask.CompletedTask;
		}
	}

	private sealed class ExecutableConformanceSubject(PluginLaunchSpec spec) : ConformanceSubject
	{
		public override async Task<ConformanceSubjectHandle> StartAsync(
			MacroDeckTestHost host,
			string? stateDirectory = null,
			PluginTestCredentials? credentials = null,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(host);
			var plugin = await host.LaunchAsync(spec).ConfigureAwait(false);
			return new ConformanceSubjectHandle { Plugin = plugin };
		}

		public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
	}

	private sealed class ArtifactConformanceSubject(string macroDeckPluginPath) : ConformanceSubject
	{
		private readonly SemaphoreSlim _resolveGate = new(1, 1);
		private PluginLaunchSpec? _spec;
		private PluginManifest? _manifest;

		public override async Task<ConformanceSubjectHandle> StartAsync(
			MacroDeckTestHost host,
			string? stateDirectory = null,
			PluginTestCredentials? credentials = null,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(host);
			var (spec, manifest) = await ResolveAsync(cancellationToken).ConfigureAwait(false);
			var plugin = await host.LaunchAsync(spec).ConfigureAwait(false);
			return new ConformanceSubjectHandle { Plugin = plugin, Manifest = manifest };
		}

		public override ValueTask DisposeAsync()
		{
			_resolveGate.Dispose();

			// Best effort: the extracted copy lives under the OS temp directory regardless, and a file
			// still open on a slow shutdown race must not turn suite cleanup into a suite failure.
			if (_spec is { WorkingDirectory: { Length: > 0 } directory } && Directory.Exists(directory))
			{
				try
				{
					Directory.Delete(directory, recursive: true);
				}
				catch (IOException)
				{
				}
				catch (UnauthorizedAccessException)
				{
				}
			}

			return ValueTask.CompletedTask;
		}

		private async Task<(PluginLaunchSpec Spec, PluginManifest Manifest)> ResolveAsync(
			CancellationToken cancellationToken)
		{
			if (_spec is { } cachedSpec && _manifest is { } cachedManifest)
			{
				return (cachedSpec, cachedManifest);
			}

			await _resolveGate.WaitAsync(cancellationToken).ConfigureAwait(false);

			try
			{
				if (_spec is { } spec && _manifest is { } manifest)
				{
					return (spec, manifest);
				}

				// PluginLaunchSpec.ForArtifactAsync resolves the manifest internally but does not hand it
				// back, so it is read once more here, through the same reader, purely to surface it on
				// ConformanceSubjectHandle.Manifest.
				var reader = new PluginArtifactReader(new PluginManifestReader(),
					Logger.None);
				var inspection = await reader.Inspect(macroDeckPluginPath, cancellationToken).ConfigureAwait(false);

				if (!inspection.Success || inspection.Manifest is not { } resolvedManifest)
				{
					throw new InvalidOperationException(
						$"'{macroDeckPluginPath}' could not be read as a plugin artifact: {inspection.ErrorMessage}");
				}

				var resolvedSpec = await PluginLaunchSpec.ForArtifactAsync(macroDeckPluginPath, cancellationToken)
					.ConfigureAwait(false);

				_spec = resolvedSpec;
				_manifest = resolvedManifest;
				return (resolvedSpec, resolvedManifest);
			}
			finally
			{
				_resolveGate.Release();
			}
		}
	}
}

/// <summary>One running instance <see cref="ConformanceSubject.StartAsync" /> produced.</summary>
public sealed class ConformanceSubjectHandle
{
	/// <summary>The running plugin. The caller owns disposing it.</summary>
	public required PluginUnderTest Plugin { get; init; }

	/// <summary>The clock driving the instance's <see cref="TimeProvider" />, when its subject kind supports one - see <see cref="ConformanceSubject.InProcess" />.</summary>
	public ManualTimeProvider? Clock { get; init; }

	/// <summary>
	/// The subject's manifest, when its subject kind has one - both <see cref="ConformanceSubject.InProcess" />
	/// (see <see cref="InProcess" />'s own <c>manifest</c> parameter) and <see cref="ConformanceSubject.Artifact" />
	/// always do; an <see cref="ConformanceSubject.Executable" /> subject never does.
	/// </summary>
	public PluginManifest? Manifest { get; init; }
}
