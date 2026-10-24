using MacroDeckHost.Integrations.AutoHotkey;
using MacroDeckHost.Integrations.AutoHotkey.Native;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class AutoHotkeyInstallationTests
{
	private const string RegistryKey = @"Software\AutoHotkey";

	private static readonly string _machineDir = Path.Combine(Path.GetTempPath(), "ahk-machine");
	private static readonly string _userDir = Path.Combine(Path.GetTempPath(), "ahk-user");
	private static readonly string _programFiles = Path.Combine(Path.GetTempPath(), "ahk-programs");

	[Test]
	public async Task The_machine_wide_registry_install_wins_over_later_candidates()
	{
		var machine = new FakeMachine { ProgramFilesFolder = _programFiles };
		machine.Registry[(AutoHotkeyRegistryScope.Machine, RegistryKey, "InstallDir")] = _machineDir;
		machine.Registry[(AutoHotkeyRegistryScope.User, RegistryKey, "InstallDir")] = _userDir;
		AddExecutable(machine, _machineDir, "AutoHotkey64.exe", 2, "2.0.30");
		AddExecutable(machine, _userDir, "AutoHotkey64.exe", 2, "2.0.18");

		var found = await AutoHotkeyInstallation.DetectAsync(machine, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(found.Status, Is.EqualTo(AutoHotkeyInstallationStatus.Valid));
			Assert.That(found.ExecutablePath, Is.EqualTo(Path.Combine(_machineDir, "v2", "AutoHotkey64.exe")));
			Assert.That(found.Version, Is.EqualTo("2.0.30"));
			Assert.That(found.WindowSpyCandidates[0], Is.EqualTo(Path.Combine(_machineDir, "UX", "WindowSpy.ahk")));
		});
	}

	[Test]
	public async Task A_32_bit_windows_uses_the_32_bit_interpreter_from_the_default_folder()
	{
		var machine = new FakeMachine { Is64BitOperatingSystem = false, ProgramFilesFolder = _programFiles };
		var installDir = Path.Combine(_programFiles, "AutoHotkey");
		AddExecutable(machine, installDir, "AutoHotkey64.exe", 2, "2.0.30");
		AddExecutable(machine, installDir, "AutoHotkey32.exe", 2, "2.0.30");

		var found = await AutoHotkeyInstallation.DetectAsync(machine, null, CancellationToken.None);

		Assert.That(found.ExecutablePath, Is.EqualTo(Path.Combine(installDir, "v2", "AutoHotkey32.exe")));
	}

	[Test]
	public async Task Version_1_is_rejected_but_a_later_version_2_candidate_is_used()
	{
		var machine = new FakeMachine { ProgramFilesFolder = _programFiles };
		machine.Registry[(AutoHotkeyRegistryScope.Machine, RegistryKey, "InstallDir")] = _machineDir;
		AddExecutable(machine, _machineDir, "AutoHotkey64.exe", 1, "1.1.37");

		var onlyVersion1 = await AutoHotkeyInstallation.DetectAsync(machine, null, CancellationToken.None);
		AddExecutable(machine, Path.Combine(_programFiles, "AutoHotkey"), "AutoHotkey64.exe", 2, "2.1.0");
		var withVersion2 = await AutoHotkeyInstallation.DetectAsync(machine, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(onlyVersion1.Status, Is.EqualTo(AutoHotkeyInstallationStatus.NotVersion2));
			Assert.That(withVersion2.Status, Is.EqualTo(AutoHotkeyInstallationStatus.Valid));
			Assert.That(withVersion2.Version, Is.EqualTo("2.1.0"));
		});
	}

	[Test]
	public async Task Nothing_installed_is_reported_as_not_found_with_a_download_link()
	{
		var found = await AutoHotkeyInstallation.DetectAsync(new FakeMachine(), null, CancellationToken.None);
		var issue = AutoHotkeyIntegration.InstallationIssue(found);

		Assert.Multiple(() =>
		{
			Assert.That(found.Status, Is.EqualTo(AutoHotkeyInstallationStatus.NotFound));
			Assert.That(issue?.HelpUrl, Is.EqualTo(new Uri("https://www.autohotkey.com/download/")));
		});
	}

	[Test]
	public async Task A_manual_portable_path_derives_the_install_folder_for_window_spy()
	{
		var portable = Path.Combine(Path.GetTempPath(), "portable-ahk");
		var inV2Folder = Path.Combine(portable, "v2", "AutoHotkey64.exe");
		var flat = Path.Combine(portable, "AutoHotkey64.exe");
		var machine = new FakeMachine();
		machine.Files[inV2Folder] = new AutoHotkeyFileVersion(2, "2.0.30");
		machine.Files[flat] = new AutoHotkeyFileVersion(2, "2.0.30");

		var nested = await AutoHotkeyInstallation.DetectAsync(machine, inV2Folder, CancellationToken.None);
		var flatResult = await AutoHotkeyInstallation.DetectAsync(machine, flat, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(nested.InstallDirectory, Is.EqualTo(portable));
			Assert.That(flatResult.InstallDirectory, Is.EqualTo(portable));
		});
	}

	[Test]
	public async Task An_executable_that_does_not_run_is_reported_as_invalid()
	{
		var machine = new FakeMachine();
		var executable = Path.Combine(_machineDir, "v2", "AutoHotkey64.exe");
		machine.Files[executable] = new AutoHotkeyFileVersion(2, "2.0.30");
		machine.NotRunnable.Add(executable);

		var found = await AutoHotkeyInstallation.DetectAsync(machine, executable, CancellationToken.None);

		Assert.That(found.Status, Is.EqualTo(AutoHotkeyInstallationStatus.NotRunnable));
	}

	private static void AddExecutable(FakeMachine machine, string installDir, string name, int major, string version)
	{
		machine.Files[Path.Combine(installDir, "v2", name)] = new AutoHotkeyFileVersion(major, version);
	}
}
