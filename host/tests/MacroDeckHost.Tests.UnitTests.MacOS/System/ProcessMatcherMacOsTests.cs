using System.Diagnostics;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Native;

namespace MacroDeckHost.Tests.UnitTests.MacOS.System;

[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class ProcessMatcherMacOsTests
{
	private static readonly string[] _launchWithArguments =
		["-a", "/Applications/App.app", "--args", "/Users/me/My File.txt", "--flag"];

	private static readonly string[] _launchWithoutArguments = ["-a", "/Applications/App.app"];

	private readonly List<Process> _started = [];
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "md-bundle-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void TearDown()
	{
		foreach (var process in _started)
		{
			process.Kill();
			process.WaitForExit();
			process.Dispose();
		}

		_started.Clear();
		Directory.Delete(_directory, true);
	}

	[Test]
	public void IsRunning_finds_a_bundle_whose_executable_has_another_name()
	{
		var bundle = WriteBundle("Long App Name.app", "mdshort1");

		Assert.That(ProcessMatcher.IsRunning(bundle), Is.False);

		Run(Path.Combine(bundle, "Contents", "MacOS", "mdshort1"));

		Assert.That(ProcessMatcher.IsRunning(bundle), Is.True);
	}

	[Test]
	public void IsRunning_reads_a_binary_info_plist()
	{
		var bundle = WriteBundle("Binary Plist.app", "mdshort2");
		var plutil = Process.Start("/usr/bin/plutil",
			["-convert", "binary1", Path.Combine(bundle, "Contents", "Info.plist")]);
		plutil.WaitForExit();

		Run(Path.Combine(bundle, "Contents", "MacOS", "mdshort2"));

		Assert.That(ProcessMatcher.IsRunning(bundle), Is.True);
	}

	[Test]
	public void Find_returns_the_bundle_process_for_a_trailing_slash_and_a_symlink()
	{
		var bundle = WriteBundle("Linked App.app", "mdshort3");
		var link = Path.Combine(_directory, "Shortcut Name.app");
		Directory.CreateSymbolicLink(link, bundle);
		var process = Run(Path.Combine(bundle, "Contents", "MacOS", "mdshort3"));

		var withSlash = ProcessMatcher.Find(bundle + "/");
		var throughLink = ProcessMatcher.Find(link);

		Assert.Multiple(() =>
		{
			Assert.That(withSlash.Select(p => p.Id), Is.EqualTo(new[] { process.Id }));
			Assert.That(throughLink.Select(p => p.Id), Is.EqualTo(new[] { process.Id }));
		});

		ProcessMatcher.Dispose(withSlash);
		ProcessMatcher.Dispose(throughLink);
	}

	[Test]
	public void IsRunning_ignores_an_unrelated_process_with_the_same_executable_name()
	{
		var bundle = WriteBundle("Some Electron App.app", "mdgeneric");
		var other = WriteBundle("Other Electron App.app", "mdgeneric");

		Run(Path.Combine(other, "Contents", "MacOS", "mdgeneric"));

		Assert.That(ProcessMatcher.IsRunning(bundle), Is.False);
	}

	[Test]
	public void IsRunning_ignores_a_helper_next_to_the_bundle_executable()
	{
		var bundle = WriteBundle("Helper App.app", "mdmain");
		var helper = Path.Combine(bundle, "Contents", "MacOS", "mdhelper");
		File.Copy("/bin/sleep", helper);

		Run(helper);

		Assert.That(ProcessMatcher.IsRunning(bundle), Is.False);
	}

	[Test]
	public void IsRunning_still_matches_a_process_named_after_the_bundle()
	{
		var executable = Path.Combine(_directory, "mdnamed");
		File.Copy("/bin/sleep", executable);
		var bundle = WriteBundle("mdnamed.app", "other");

		Run(executable);

		Assert.That(ProcessMatcher.IsRunning(bundle), Is.True);
	}

	[Test]
	public void IsRunning_tolerates_paths_that_are_no_bundle()
	{
		var plainFile = Path.Combine(_directory, "Plain File.app");
		File.WriteAllText(plainFile, "not a bundle");
		var emptyDirectory = Path.Combine(_directory, "Empty.app");
		Directory.CreateDirectory(emptyDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(ProcessMatcher.IsRunning(Path.Combine(_directory, "Missing.app")), Is.False);
			Assert.That(ProcessMatcher.IsRunning(plainFile), Is.False);
			Assert.That(ProcessMatcher.IsRunning(emptyDirectory), Is.False);
		});
	}

	[Test]
	public void Bundle_launch_keeps_quoted_arguments_together()
	{
		var startInfo = MacOsApplicationService.CreateBundleLaunch("/Applications/App.app",
			"\"/Users/me/My File.txt\" --flag",
			"/tmp");

		Assert.Multiple(() =>
		{
			Assert.That(startInfo.FileName, Is.EqualTo("open"));
			Assert.That(startInfo.ArgumentList,
				Is.EqualTo(_launchWithArguments));
			Assert.That(startInfo.WorkingDirectory, Is.EqualTo("/tmp"));
		});
	}

	[Test]
	public void A_process_outside_appkit_cannot_be_asked_to_quit()
	{
		var process = Run("/bin/sleep");

		Assert.That(MacOsRunningApplication.TryTerminate(process.Id), Is.False);
	}

	[TestCase(null)]
	[TestCase("   ")]
	public void Bundle_launch_without_arguments_passes_none(string? arguments)
	{
		var startInfo = MacOsApplicationService.CreateBundleLaunch("/Applications/App.app", arguments, null);

		Assert.That(startInfo.ArgumentList, Is.EqualTo(_launchWithoutArguments));
	}

	private string WriteBundle(string name, string executable)
	{
		var bundle = Path.Combine(_directory, name);
		var macOs = Path.Combine(bundle, "Contents", "MacOS");
		Directory.CreateDirectory(macOs);
		File.Copy("/bin/sleep", Path.Combine(macOs, executable));
		File.WriteAllText(Path.Combine(bundle, "Contents", "Info.plist"),
			$"""
			 <?xml version="1.0" encoding="UTF-8"?>
			 <plist version="1.0"><dict>
			 <key>CFBundleExecutable</key><string>{executable}</string>
			 <key>CFBundleIdentifier</key><string>app.macro-deck.test.{Guid.NewGuid():N}</string>
			 <key>CFBundlePackageType</key><string>APPL</string>
			 </dict></plist>
			 """);
		return bundle;
	}

	private Process Run(string executable)
	{
		var process = Process.Start(executable, ["30"]);
		_started.Add(process);
		Thread.Sleep(100);
		return process;
	}
}
