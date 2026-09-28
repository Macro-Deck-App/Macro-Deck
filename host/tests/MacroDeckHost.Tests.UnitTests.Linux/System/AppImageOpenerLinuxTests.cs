using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class AppImageOpenerLinuxTests
{
	private string _root = null!;
	private string _appDir = null!;
	private string _systemBin = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Directory.CreateTempSubdirectory("md-appimage-").FullName;
		_appDir = Path.Combine(_root, "mount");
		_systemBin = Path.Combine(_root, "system-bin");
		Directory.CreateDirectory(Path.Combine(_appDir, "usr", "bin"));
		Directory.CreateDirectory(_systemBin);
	}

	[TearDown]
	public void TearDown() => Directory.Delete(_root, recursive: true);

	private static string Executable(string directory, string name)
	{
		var path = Path.Combine(directory, name);
		File.WriteAllText(path, "#!/bin/sh\n");
		File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		return path;
	}

	private Dictionary<string, string> AppImageEnvironmentWith(string path) => new()
	{
		["APPDIR"] = _appDir,
		["PATH"] = path,
		["LD_LIBRARY_PATH"] = $"{_appDir}/usr/lib/:",
		["GTK_THEME"] = "Adwaita:light",
		["HOME"] = _root,
		["KDE_SESSION_VERSION"] = "6"
	};

	[Test]
	public void A_link_reaches_the_system_xdg_open_with_the_environment_outside_the_bundle()
	{
		Executable(Path.Combine(_appDir, "usr", "bin"), "xdg-open");
		var system = Executable(_systemBin, "xdg-open");

		var opener = AppImageEnvironment.TryCreateOpener(
			"https://example.com",
			AppImageEnvironmentWith($"{_appDir}/usr/bin/:{_systemBin}"));

		Assert.That(opener, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(opener!.FileName, Is.EqualTo(system));
			Assert.That(opener.ArgumentList, Is.EqualTo((List<string>)["https://example.com"]));
			Assert.That(opener.UseShellExecute, Is.False);
			Assert.That(opener.WorkingDirectory, Is.EqualTo(_root));
			Assert.That(opener.Environment["PATH"], Is.EqualTo(_systemBin));
			Assert.That(opener.Environment["KDE_SESSION_VERSION"], Is.EqualTo("6"));
			Assert.That(opener.Environment.Keys, Has.None.AnyOf("APPDIR", "LD_LIBRARY_PATH", "GTK_THEME"));
		});
	}

	[Test]
	public void Without_xdg_open_the_next_system_launcher_is_used()
	{
		var gio = Executable(_systemBin, "gio");

		var opener = AppImageEnvironment.TryCreateOpener("/home/u/Documents", AppImageEnvironmentWith(_systemBin));

		Assert.That(opener, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(opener!.FileName, Is.EqualTo(gio));
			Assert.That(opener.ArgumentList, Is.EqualTo((List<string>)["open", "/home/u/Documents"]));
		});
	}

	[Test]
	public void A_system_without_any_launcher_falls_back_to_the_bundled_one_outside_the_bundle_environment()
	{
		var bundled = Executable(Path.Combine(_appDir, "usr", "bin"), "xdg-open");

		var opener = AppImageEnvironment.TryCreateOpener("https://example.com", AppImageEnvironmentWith($"{_appDir}/usr/bin/:{_systemBin}"));

		Assert.That(opener, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(opener!.FileName, Is.EqualTo(bundled));
			Assert.That(opener.Environment.Keys, Has.None.EqualTo("LD_LIBRARY_PATH"));
		});
	}

	[Test]
	public void No_launcher_anywhere_leaves_the_caller_on_its_own_path()
		=> Assert.That(AppImageEnvironment.TryCreateOpener("https://example.com", AppImageEnvironmentWith(_systemBin)), Is.Null);

	[Test]
	public void A_target_that_looks_like_an_option_is_passed_as_a_path()
	{
		Executable(_systemBin, "xdg-open");

		var opener = AppImageEnvironment.TryCreateOpener("--help", AppImageEnvironmentWith(_systemBin));

		Assert.That(opener!.ArgumentList, Is.EqualTo((List<string>)["./--help"]));
	}

	[Test]
	public void Entries_under_the_real_path_of_a_symlinked_bundle_are_removed()
	{
		var link = Path.Combine(_root, "link");
		File.CreateSymbolicLink(link, _appDir);

		var roots = AppImageEnvironment.BundleRoots(link);
		var outside = AppImageEnvironment.Outside(
			new Dictionary<string, string> { ["PATH"] = $"{_appDir}/usr/bin:{_systemBin}" },
			roots);

		Assert.That(outside["PATH"], Is.EqualTo(_systemBin));
	}

	[Test]
	public void An_executable_file_keeps_running_instead_of_being_opened()
	{
		var script = Executable(_root, "run.sh");

		Assert.That(LinuxApplicationService.OpensWithDefaultApplication(script), Is.False);
	}

	[Test]
	public void Documents_folders_and_web_addresses_go_to_the_default_application()
	{
		var document = Path.Combine(_root, "notes.txt");
		File.WriteAllText(document, "notes");
		var linkedDocument = Path.Combine(_root, "linked.txt");
		File.CreateSymbolicLink(linkedDocument, document);

		Assert.Multiple(() =>
		{
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication(document), Is.True);
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication(linkedDocument), Is.True);
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication(new Uri(document).AbsoluteUri), Is.True);
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication(_root), Is.True);
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication("https://example.com"), Is.True);
		});
	}

	[Test]
	public void A_bare_or_relative_name_is_left_to_the_platform_lookup()
		=> Assert.Multiple(() =>
		{
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication("firefox"), Is.False);
			Assert.That(LinuxApplicationService.OpensWithDefaultApplication("docs/notes.txt"), Is.False);
		});
}
