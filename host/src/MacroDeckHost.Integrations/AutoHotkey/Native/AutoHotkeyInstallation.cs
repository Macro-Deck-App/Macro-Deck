namespace MacroDeckHost.Integrations.AutoHotkey.Native;

internal enum AutoHotkeyInstallationStatus
{
	Valid,
	NotFound,
	NotVersion2,
	NotRunnable
}

internal sealed record AutoHotkeyInstallation(
	AutoHotkeyInstallationStatus Status,
	string? ExecutablePath = null,
	string? InstallDirectory = null,
	string? Version = null)
{
	private const string RegistryKey = @"Software\AutoHotkey";
	private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AutoHotkey";
	private const string Version2Folder = "v2";

	public static AutoHotkeyInstallation NotFound { get; } = new(AutoHotkeyInstallationStatus.NotFound);

	public bool IsValid => Status == AutoHotkeyInstallationStatus.Valid;

	public IReadOnlyList<string> WindowSpyCandidates
		=> InstallDirectory is null
			? []
			:
			[
				Path.Combine(InstallDirectory, "UX", "WindowSpy.ahk"),
				Path.Combine(InstallDirectory, "WindowSpy.ahk")
			];

	public static async Task<AutoHotkeyInstallation> DetectAsync(
		IAutoHotkeyMachine machine,
		string? manualExecutable,
		CancellationToken cancellationToken)
	{
		if (!string.IsNullOrWhiteSpace(manualExecutable))
		{
			var executable = manualExecutable.Trim();
			return machine.FileExists(executable)
				? await ValidateAsync(machine, executable, InstallDirectoryOf(executable), cancellationToken)
					.ConfigureAwait(false)
				: NotFound;
		}

		var best = NotFound;
		foreach (var directory in CandidateDirectories(machine))
		{
			var executable = Path.Combine(directory, Version2Folder, ExecutableName(machine));
			if (!machine.FileExists(executable))
			{
				continue;
			}

			var result = await ValidateAsync(machine, executable, directory, cancellationToken).ConfigureAwait(false);
			if (result.IsValid)
			{
				return result;
			}

			if (Rank(result.Status) > Rank(best.Status))
			{
				best = result;
			}
		}

		return best;
	}

	internal static string InstallDirectoryOf(string executable)
	{
		var folder = Path.GetDirectoryName(executable) ?? string.Empty;
		var parent = Path.GetDirectoryName(folder);
		return string.Equals(Path.GetFileName(folder), Version2Folder, StringComparison.OrdinalIgnoreCase)
			&& !string.IsNullOrEmpty(parent)
				? parent
				: folder;
	}

	internal static IReadOnlyList<string> CandidateDirectories(IAutoHotkeyMachine machine)
	{
		var candidates = new List<string>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void Add(string? directory)
		{
			if (string.IsNullOrWhiteSpace(directory))
			{
				return;
			}

			var trimmed = directory.Trim().Trim('"').TrimEnd('\\', '/');
			if (trimmed.Length > 0 && seen.Add(trimmed))
			{
				candidates.Add(trimmed);
			}
		}

		Add(machine.ReadRegistryString(AutoHotkeyRegistryScope.Machine, RegistryKey, "InstallDir"));
		Add(machine.ReadRegistryString(AutoHotkeyRegistryScope.User, RegistryKey, "InstallDir"));
		Add(machine.ReadRegistryString(AutoHotkeyRegistryScope.Machine, UninstallKey, "InstallLocation"));
		Add(machine.ReadRegistryString(AutoHotkeyRegistryScope.User, UninstallKey, "InstallLocation"));

		if (machine.ProgramFilesFolder is { } programFiles)
		{
			Add(Path.Combine(programFiles, "AutoHotkey"));
		}

		if (machine.LocalAppDataFolder is { } localAppData)
		{
			Add(Path.Combine(localAppData, "Programs", "AutoHotkey"));
		}

		return candidates;
	}

	private static string ExecutableName(IAutoHotkeyMachine machine)
	{
		return machine.Is64BitOperatingSystem ? "AutoHotkey64.exe" : "AutoHotkey32.exe";
	}

	private static async Task<AutoHotkeyInstallation> ValidateAsync(
		IAutoHotkeyMachine machine,
		string executable,
		string installDirectory,
		CancellationToken cancellationToken)
	{
		var version = machine.ReadFileVersion(executable);
		if (version is null || version.Major != 2)
		{
			return new AutoHotkeyInstallation(AutoHotkeyInstallationStatus.NotVersion2,
				executable,
				installDirectory,
				version?.Display);
		}

		var status = await machine.CanRunAsync(executable, cancellationToken).ConfigureAwait(false)
			? AutoHotkeyInstallationStatus.Valid
			: AutoHotkeyInstallationStatus.NotRunnable;
		return new AutoHotkeyInstallation(status, executable, installDirectory, version.Display);
	}

	private static int Rank(AutoHotkeyInstallationStatus status)
	{
		return status switch
		{
			AutoHotkeyInstallationStatus.NotRunnable => 2,
			AutoHotkeyInstallationStatus.NotVersion2 => 1,
			_ => 0
		};
	}
}
