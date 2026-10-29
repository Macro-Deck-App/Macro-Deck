using System.Diagnostics;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ProcessMatcherTests
{
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "md-process-matcher-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_directory, true);
	}

	[Test]
	public void Find_returns_empty_for_blank_path()
	{
		Assert.That(ProcessMatcher.Find("   "), Is.Empty);
	}

	[Test]
	public void IsRunning_returns_false_for_unknown_path()
	{
		Assert.That(ProcessMatcher.IsRunning("/path/that/does/not/exist/xyz123.bin"), Is.False);
	}

	[Test]
	public void Find_locates_the_current_process_by_its_executable_path()
	{
		var executablePath = CurrentExecutablePath();

		var matches = ProcessMatcher.Find(executablePath);

		Assert.That(matches.Select(p => p.Id), Does.Contain(Environment.ProcessId));
	}

	[Test]
	public void IsRunning_finds_the_running_target_of_a_shortcut()
	{
		var shortcut = WriteShortcut("Some App.lnk", new ShellLinkBuilder { LocalBasePath = CurrentExecutablePath() });

		Assert.That(ProcessMatcher.IsRunning(shortcut), Is.True);
	}

	[Test]
	public void IsRunning_ignores_the_target_of_a_shortcut_that_passes_arguments()
	{
		var shortcut = WriteShortcut("Some App.lnk",
			new ShellLinkBuilder { LocalBasePath = CurrentExecutablePath(), Arguments = "/k setup.bat" });

		Assert.That(ProcessMatcher.IsRunning(shortcut), Is.False);
	}

	[Test]
	public void IsRunning_still_matches_a_shortcut_named_after_the_running_process()
	{
		using var current = Process.GetCurrentProcess();
		var shortcut = WriteShortcut(current.ProcessName + ".lnk",
			new ShellLinkBuilder { LocalBasePath = CurrentExecutablePath(), Arguments = "--other" });

		Assert.That(ProcessMatcher.IsRunning(shortcut), Is.True);
	}

	[Test]
	public void IsRunning_returns_false_for_a_shortcut_that_cannot_be_parsed()
	{
		var shortcut = Path.Combine(_directory, "Broken App.lnk");
		File.WriteAllBytes(shortcut, [1, 2, 3]);

		Assert.That(ProcessMatcher.IsRunning(shortcut), Is.False);
	}

	[Test]
	public void TryGetExecutablePath_never_throws_for_any_process()
	{
		Assert.DoesNotThrow(() =>
		{
			foreach (var process in Process.GetProcesses())
			{
				_ = ProcessMatcher.TryGetExecutablePath(process);
			}
		});
	}

	private static string CurrentExecutablePath()
	{
		var executablePath = Environment.ProcessPath;
		Assume.That(executablePath, Is.Not.Null.And.Not.Empty);
		return executablePath!;
	}

	private string WriteShortcut(string fileName, ShellLinkBuilder builder)
	{
		var path = Path.Combine(_directory, fileName);
		File.WriteAllBytes(path, builder.Build());
		return path;
	}
}
