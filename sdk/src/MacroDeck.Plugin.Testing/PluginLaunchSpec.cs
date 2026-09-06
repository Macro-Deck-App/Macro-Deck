using System.Diagnostics;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;
using Serilog.Core;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// What <see cref="MacroDeckTestHost.LaunchAsync" /> should run: an executable, a framework-dependent
/// assembly launched through the <c>dotnet</c> muxer, or a plugin resolved from an installed version
/// directory or a packed <c>.macroDeckPlugin</c> artifact.
/// </summary>
public sealed class PluginLaunchSpec
{
	private PluginLaunchSpec(string executablePath, IReadOnlyList<string> arguments, string workingDirectory)
	{
		ExecutablePath = executablePath;
		Arguments = arguments;
		WorkingDirectory = workingDirectory;
	}

	internal string ExecutablePath { get; }

	internal IReadOnlyList<string> Arguments { get; }

	internal string WorkingDirectory { get; }

	/// <summary>
	/// Extra environment variables to set on the launched process - e.g. a test-specific flag a
	/// third-party plugin's own <c>Program.cs</c> reads. Empty by default. Merged into the child
	/// process's environment before <see cref="MacroDeckTestHost.LaunchAsync" /> writes its own required
	/// <c>MACRO_DECK_PLUGIN_*</c> values, so an entry here can never accidentally override one of those
	/// (see <see cref="MacroDeck.Plugin.Hosting.Configuration.PluginEnvironmentConfiguration.KeysByVariable" />) -
	/// <see cref="MacroDeckTestHost.LaunchAsync" /> must be able to reach the process it just
	/// started regardless of what a caller puts here. Setting a variable on the current process instead
	/// (<see cref="System.Environment.SetEnvironmentVariable(string, string?)" />) would leak it into
	/// every other child process the same test run launches, not just this one.
	/// </summary>
	public IReadOnlyDictionary<string, string?> Environment { get; set; }
		= new Dictionary<string, string?>(StringComparer.Ordinal);

	/// <summary>Launches an already-built executable directly.</summary>
	public static PluginLaunchSpec ForExecutable(string executablePath, params string[] arguments)
	{
		ArgumentException.ThrowIfNullOrEmpty(executablePath);
		ArgumentNullException.ThrowIfNull(arguments);

		return new PluginLaunchSpec(executablePath, arguments, WorkingDirectoryOf(executablePath));
	}

	/// <summary>Launches a managed assembly through the <c>dotnet</c> muxer, e.g. <c>dotnet MyPlugin.dll</c>.</summary>
	public static PluginLaunchSpec ForDotnet(string assemblyPath, params string[] arguments)
	{
		ArgumentException.ThrowIfNullOrEmpty(assemblyPath);
		ArgumentNullException.ThrowIfNull(arguments);

		return new PluginLaunchSpec(DotnetMuxerPath(), [assemblyPath, .. arguments], WorkingDirectoryOf(assemblyPath));
	}

	/// <summary>
	/// Launches the entrypoint an already-read <paramref name="manifest" /> declares for the running
	/// machine's runtime identifier (falling back the same way <see cref="PluginRuntimeIdentifiers.CandidatesFor" />
	/// does), resolved against <paramref name="versionDirectory" /> - the directory the manifest itself
	/// lives in.
	/// </summary>
	/// <exception cref="InvalidOperationException">
	/// The manifest declares no entrypoint for this machine's runtime identifier or its fallbacks.
	/// </exception>
	public static PluginLaunchSpec ForManifest(string versionDirectory, PluginManifest manifest)
	{
		ArgumentException.ThrowIfNullOrEmpty(versionDirectory);
		ArgumentNullException.ThrowIfNull(manifest);

		var entrypoint = PluginRuntimeIdentifiers.CandidatesFor(PluginRuntimeIdentifiers.Current)
				.Select(rid => manifest.Entrypoints.GetValueOrDefault(rid))
				.FirstOrDefault(candidate => candidate is not null) ??
			throw new InvalidOperationException(
				$"The manifest for '{manifest.Id}' {manifest.Version} declares no entrypoint for " +
				$"'{PluginRuntimeIdentifiers.Current}' or its fallbacks.");

		var executablePath = Path.Combine(versionDirectory, entrypoint.Executable);
		var arguments = entrypoint.Arguments ?? [];
		var isFrameworkDependent = entrypoint.Runtime?.Kind == PluginEntrypointRuntimeKind.FrameworkDependent;

		// A self-contained entrypoint is launched directly, so it must actually be executable. A real
		// installed version directory already has this set (the supervisor's own installer sets it during
		// activation), but ForArtifactAsync extracts straight from a .macroDeckPlugin via
		// IPluginArtifactReader.ExtractTo, which - correctly - never touches mode bits (that is an
		// installation decision, not an extraction one), so a freshly extracted archive's entrypoint is
		// ordinary non-executable file content on Unix. Reasserting the bit here, once, for whichever single
		// entrypoint is actually about to be launched, is harmless on an already-executable file and is what
		// makes an artifact subject launchable at all without one.
		if (!isFrameworkDependent && File.Exists(executablePath))
		{
			EnsureExecutable(executablePath);
		}

		return isFrameworkDependent
			? new PluginLaunchSpec(DotnetMuxerPath(), [executablePath, .. arguments], versionDirectory)
			: new PluginLaunchSpec(executablePath, arguments, versionDirectory);
	}

	/// <summary>
	/// Extracts <paramref name="macroDeckPluginPath" /> into a fresh temporary directory and resolves its
	/// entrypoint the same way <see cref="ForManifest" /> does. The extracted directory is not cleaned up
	/// automatically - the launched process needs it for its whole lifetime, and by the time this method
	/// returns there is no longer a safe point at which nothing could still be reading from it.
	/// </summary>
	public static async Task<PluginLaunchSpec> ForArtifactAsync(
		string macroDeckPluginPath,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(macroDeckPluginPath);

		var reader = new PluginArtifactReader(new PluginManifestReader(), Logger.None);
		var inspection = await reader.Inspect(macroDeckPluginPath, cancellationToken).ConfigureAwait(false);

		if (!inspection.Success || inspection.Manifest is not { } manifest)
		{
			throw new InvalidOperationException(
				$"'{macroDeckPluginPath}' could not be read as a plugin artifact: {inspection.ErrorMessage}");
		}

		var targetDirectory = Directory.CreateTempSubdirectory("macrodeck-plugin-test-artifact-").FullName;
		var extraction = await reader.ExtractTo(macroDeckPluginPath, targetDirectory, cancellationToken)
			.ConfigureAwait(false);

		if (!extraction.Success)
		{
			throw new InvalidOperationException(
				$"'{macroDeckPluginPath}' could not be extracted: {extraction.ErrorMessage}");
		}

		return ForManifest(targetDirectory, manifest);
	}

	private static string WorkingDirectoryOf(string path)
		=> Path.GetDirectoryName(Path.GetFullPath(path)) ?? System.Environment.CurrentDirectory;

	/// <summary>Adds the executable bit for a self-contained entrypoint - see <see cref="ForManifest" />'s
	/// own remarks. A no-op on Windows, which has no such bit. A file system without mode bits, or one this
	/// process cannot change the mode of, is not a reason to fail here either: the subsequent process
	/// launch will fail with its own clear error if the file genuinely cannot be executed.</summary>
	private static void EnsureExecutable(string path)
	{
		if (OperatingSystem.IsWindows())
		{
			return;
		}

		try
		{
			const UnixFileMode executable = UnixFileMode.UserRead |
				UnixFileMode.UserWrite |
				UnixFileMode.UserExecute |
				UnixFileMode.GroupRead |
				UnixFileMode.GroupExecute |
				UnixFileMode.OtherRead |
				UnixFileMode.OtherExecute;

			File.SetUnixFileMode(path, File.GetUnixFileMode(path) | executable);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
		{
		}
	}

	private static string DotnetMuxerPath()
	{
		var current = Process.GetCurrentProcess().MainModule?.FileName;

		if (!string.IsNullOrEmpty(current) &&
			string.Equals(Path.GetFileNameWithoutExtension(current), "dotnet", StringComparison.OrdinalIgnoreCase))
		{
			return current;
		}

		return OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
	}
}
