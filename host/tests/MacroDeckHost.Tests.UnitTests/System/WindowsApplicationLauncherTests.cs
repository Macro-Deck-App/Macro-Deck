using System.ComponentModel;
using System.Diagnostics;
using MacroDeckHost.Integrations.System.Application;

namespace MacroDeckHost.Tests.UnitTests.System;

public class WindowsApplicationLauncherTests
{
	private const int ErrorCantAccessFile = 1920;
	private const string AliasPath = @"C:\Users\someone\AppData\Local\Microsoft\WindowsApps\Spotify.exe";

	[Test]
	public void Starts_through_the_shell_when_the_shell_can_launch_the_application()
	{
		var attempts = new List<ProcessStartInfo>();

		WindowsApplicationLauncher.Start(AliasPath, null, null, runAsAdmin: false, Record(attempts));

		Assert.That(attempts, Has.Count.EqualTo(1));
		Assert.That(attempts[0].UseShellExecute, Is.True);
		Assert.That(attempts[0].FileName, Is.EqualTo(AliasPath));
	}

	[Test]
	public void Requests_elevation_through_the_shell_when_the_application_runs_as_admin()
	{
		var attempts = new List<ProcessStartInfo>();

		WindowsApplicationLauncher.Start(AliasPath, null, null, runAsAdmin: true, Record(attempts));

		Assert.That(attempts, Has.Count.EqualTo(1));
		Assert.That(attempts[0].Verb, Is.EqualTo("runas"));
	}

	[Test]
	public void Launches_an_application_the_shell_cannot_access_without_the_shell()
	{
		var attempts = new List<ProcessStartInfo>();
		var start = FailFirstWith(new Win32Exception(ErrorCantAccessFile), attempts);

		WindowsApplicationLauncher.Start(AliasPath, "--minimized", @"C:\work", runAsAdmin: false, start);

		Assert.That(attempts, Has.Count.EqualTo(2));
		Assert.That(attempts[1].UseShellExecute, Is.False);
		Assert.That(attempts[1].FileName, Is.EqualTo(AliasPath));
		Assert.That(attempts[1].Arguments, Is.EqualTo("--minimized"));
		Assert.That(attempts[1].WorkingDirectory, Is.EqualTo(@"C:\work"));
	}

	[Test]
	public void Reports_a_failure_that_is_not_the_shell_being_unable_to_access_the_file()
	{
		var attempts = new List<ProcessStartInfo>();
		var start = FailFirstWith(new Win32Exception(5), attempts);

		Assert.Throws<Win32Exception>(()
			=> WindowsApplicationLauncher.Start(AliasPath, null, null, runAsAdmin: false, start));
		Assert.That(attempts, Has.Count.EqualTo(1));
	}

	[Test]
	public void Reports_a_failure_instead_of_launching_unelevated_when_the_application_runs_as_admin()
	{
		var attempts = new List<ProcessStartInfo>();
		var start = FailFirstWith(new Win32Exception(ErrorCantAccessFile), attempts);

		Assert.Throws<Win32Exception>(()
			=> WindowsApplicationLauncher.Start(AliasPath, null, null, runAsAdmin: true, start));
		Assert.That(attempts, Has.Count.EqualTo(1));
	}

	private static Func<ProcessStartInfo, Process?> Record(List<ProcessStartInfo> attempts)
		=> startInfo =>
		{
			attempts.Add(startInfo);
			return null;
		};

	private static Func<ProcessStartInfo, Process?> FailFirstWith(
		Exception exception,
		List<ProcessStartInfo> attempts)
		=> startInfo =>
		{
			attempts.Add(startInfo);
			if (attempts.Count == 1)
			{
				throw exception;
			}

			return null;
		};
}
