using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Platform.Processes;

namespace MacroDeckHost.Tests.UnitTests.Processes;

[TestFixture]
[Platform(Exclude = "Win")]
[UnsupportedOSPlatform("windows")]
public class DetachedProcessTests
{
	[Test]
	public void A_program_that_does_not_exist_fails_the_start_itself()
	{
		Assert.That(() => DetachedProcess.Spawn(new ProcessStartInfo($"macro-deck-no-such-program-{Guid.NewGuid():N}")),
			Throws.InstanceOf<Win32Exception>());
	}

	[Test]
	public void A_working_directory_that_does_not_exist_fails_the_start_itself()
	{
		var startInfo = new ProcessStartInfo("sh")
		{
			ArgumentList = { "-c", "exit 0" },
			WorkingDirectory = Path.Combine(Path.GetTempPath(), $"macro-deck-gone-{Guid.NewGuid():N}")
		};

		Assert.That(() => DetachedProcess.Spawn(startInfo), Throws.InstanceOf<Win32Exception>());
	}

	[Test]
	public async Task The_exit_code_of_a_detached_program_is_reported()
	{
		var exitCode = await DetachedProcess.Spawn(new ProcessStartInfo("sh") { ArgumentList = { "-c", "exit 7" } })
			.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(exitCode, Is.EqualTo(7));
	}

	[Test]
	public async Task A_detached_program_that_finished_leaves_no_zombie_behind()
	{
		var pidFile = Path.Combine(Path.GetTempPath(), $"md-detached-{Guid.NewGuid():N}.pid");
		try
		{
			await DetachedProcess
				.Spawn(new ProcessStartInfo("sh") { ArgumentList = { "-c", $"echo $$ > '{pidFile}'" } })
				.WaitAsync(TimeSpan.FromSeconds(10));
			var pid = int.Parse((await File.ReadAllTextAsync(pidFile)).Trim(),
				global::System.Globalization.CultureInfo.InvariantCulture);

			Assert.That(Kill(pid, 0), Is.EqualTo(-1), "a reaped child no longer exists, a zombie still would");
		}
		finally
		{
			File.Delete(pidFile);
		}
	}

	[DllImport("libc", EntryPoint = "kill", SetLastError = true)]
	private static extern int Kill(int pid, int signal);
}
