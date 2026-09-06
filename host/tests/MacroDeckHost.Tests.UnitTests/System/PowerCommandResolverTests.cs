using MacroDeckHost.Integrations.System.Power;

namespace MacroDeckHost.Tests.UnitTests.System;

public class PowerCommandResolverTests
{
	[Test]
	public void MacOs_resolves_lock_to_cgsession_suspend()
	{
		var (fileName, arguments) = MacOsPowerCommandResolver.Resolve(PowerOperation.Lock, force: false);
		string[] expectedArguments = ["-suspend"];

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo(MacOsPowerCommandResolver.CgSessionPath));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[Test]
	public void MacOs_resolves_sleep_to_pmset_sleepnow()
	{
		var (fileName, arguments) = MacOsPowerCommandResolver.Resolve(PowerOperation.Sleep, force: false);
		string[] expectedArguments = ["sleepnow"];

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("pmset"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[TestCase(false)]
	[TestCase(true)]
	public void MacOs_resolves_restart_to_osascript_system_events_ignoring_force(bool force)
	{
		var (fileName, arguments) = MacOsPowerCommandResolver.Resolve(PowerOperation.Restart, force);
		string[] expectedArguments = ["-e", "tell application \"System Events\" to restart"];

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("osascript"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[TestCase(false)]
	[TestCase(true)]
	public void MacOs_resolves_shut_down_to_osascript_system_events_ignoring_force(bool force)
	{
		var (fileName, arguments) = MacOsPowerCommandResolver.Resolve(PowerOperation.ShutDown, force);
		string[] expectedArguments = ["-e", "tell application \"System Events\" to shut down"];

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("osascript"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[Test]
	public void MacOs_resolve_throws_for_hibernate()
	{
		Assert.Throws<NotSupportedException>(() => MacOsPowerCommandResolver.Resolve(PowerOperation.Hibernate, false));
	}

	[Test]
	public void Linux_resolves_lock_to_loginctl_when_available()
	{
		var (fileName, arguments) = LinuxPowerCommandResolver.ResolveLock(hasLoginctl: true);
		string[] expectedArguments = ["lock-session"];

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("loginctl"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[Test]
	public void Linux_resolves_lock_to_xdg_screensaver_fallback()
	{
		var (fileName, arguments) = LinuxPowerCommandResolver.ResolveLock(hasLoginctl: false);
		string[] expectedArguments = ["lock"];

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("xdg-screensaver"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[TestCase(PowerOperation.Sleep, "suspend")]
	[TestCase(PowerOperation.Hibernate, "hibernate")]
	[TestCase(PowerOperation.Restart, "reboot")]
	[TestCase(PowerOperation.ShutDown, "poweroff")]
	public void Linux_resolves_verb_via_systemctl_when_available(PowerOperation operation, string verb)
	{
		var (fileName, arguments) = LinuxPowerCommandResolver.Resolve(operation, force: false, useSystemctl: true);
		var expectedArguments = new[] { verb };

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("systemctl"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[TestCase(PowerOperation.Sleep, "suspend")]
	[TestCase(PowerOperation.Hibernate, "hibernate")]
	[TestCase(PowerOperation.Restart, "reboot")]
	[TestCase(PowerOperation.ShutDown, "poweroff")]
	public void Linux_falls_back_to_loginctl_when_systemctl_unavailable(PowerOperation operation, string verb)
	{
		var (fileName, arguments) = LinuxPowerCommandResolver.Resolve(operation, force: false, useSystemctl: false);
		var expectedArguments = new[] { verb };

		Assert.Multiple(() =>
		{
			Assert.That(fileName, Is.EqualTo("loginctl"));
			Assert.That(arguments, Is.EqualTo(expectedArguments));
		});
	}

	[TestCase(PowerOperation.Restart, "reboot")]
	[TestCase(PowerOperation.ShutDown, "poweroff")]
	public void Linux_force_appends_ignore_inhibitors_flag_never_force(PowerOperation operation, string verb)
	{
		var (_, arguments) = LinuxPowerCommandResolver.Resolve(operation, force: true, useSystemctl: true);
		var expectedArguments = new[] { verb, "-i" };

		Assert.Multiple(() =>
		{
			Assert.That(arguments, Is.EqualTo(expectedArguments));
			Assert.That(arguments, Has.None.EqualTo("--force"));
		});
	}

	[Test]
	public void Linux_resolve_throws_for_lock()
	{
		Assert.Throws<NotSupportedException>(() =>
			LinuxPowerCommandResolver.Resolve(PowerOperation.Lock, force: false, useSystemctl: true));
	}
}
