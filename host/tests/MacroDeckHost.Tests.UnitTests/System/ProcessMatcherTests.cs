using System.Diagnostics;
using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Tests.UnitTests.System;

public class ProcessMatcherTests
{
	[Test]
	public void Find_returns_empty_for_blank_path()
		=> Assert.That(ProcessMatcher.Find("   "), Is.Empty);

	[Test]
	public void IsRunning_returns_false_for_unknown_path()
		=> Assert.That(ProcessMatcher.IsRunning("/path/that/does/not/exist/xyz123.bin"), Is.False);

	[Test]
	public void Find_locates_the_current_process_by_its_executable_path()
	{
		var executablePath = Environment.ProcessPath;
		Assume.That(executablePath, Is.Not.Null.And.Not.Empty);

		var matches = ProcessMatcher.Find(executablePath!);

		Assert.That(matches.Select(p => p.Id), Does.Contain(Environment.ProcessId));
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
}
