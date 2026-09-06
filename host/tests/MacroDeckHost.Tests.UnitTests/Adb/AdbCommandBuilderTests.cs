using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbCommandBuilderTests
{
	private const string Serial = "R58M12ABCDE";

	[TestCase(AdbKey.Home, "KEYCODE_HOME")]
	[TestCase(AdbKey.Back, "KEYCODE_BACK")]
	[TestCase(AdbKey.Enter, "KEYCODE_ENTER")]
	[TestCase(AdbKey.Menu, "KEYCODE_MENU")]
	[TestCase(AdbKey.Search, "KEYCODE_SEARCH")]
	[TestCase(AdbKey.VolumeUp, "KEYCODE_VOLUME_UP")]
	[TestCase(AdbKey.VolumeDown, "KEYCODE_VOLUME_DOWN")]
	[TestCase(AdbKey.VolumeMute, "KEYCODE_VOLUME_MUTE")]
	[TestCase(AdbKey.MediaPlayPause, "KEYCODE_MEDIA_PLAY_PAUSE")]
	[TestCase(AdbKey.MediaNext, "KEYCODE_MEDIA_NEXT")]
	[TestCase(AdbKey.MediaPrevious, "KEYCODE_MEDIA_PREVIOUS")]
	[TestCase(AdbKey.Power, "KEYCODE_POWER")]
	[TestCase(AdbKey.Sleep, "KEYCODE_SLEEP")]
	[TestCase(AdbKey.Wakeup, "KEYCODE_WAKEUP")]
	[TestCase(AdbKey.AppSwitch, "KEYCODE_APP_SWITCH")]
	[TestCase(AdbKey.DpadUp, "KEYCODE_DPAD_UP")]
	[TestCase(AdbKey.DpadDown, "KEYCODE_DPAD_DOWN")]
	[TestCase(AdbKey.DpadLeft, "KEYCODE_DPAD_LEFT")]
	[TestCase(AdbKey.DpadRight, "KEYCODE_DPAD_RIGHT")]
	[TestCase(AdbKey.DpadCenter, "KEYCODE_DPAD_CENTER")]
	public void KeyEvent_maps_every_key_to_its_fixed_keycode(AdbKey key, string keyCode)
	{
		var result = AdbCommandBuilder.Build(new AdbKeyEventCommand(Serial, key));

		AssertShellCommand(result, $"input keyevent {keyCode}");
	}

	[Test]
	public void StartApp_builds_a_monkey_launch_command()
	{
		var result = AdbCommandBuilder.Build(new AdbStartAppCommand(Serial, "com.example.app"));

		AssertShellCommand(result, "monkey -p 'com.example.app' -c android.intent.category.LAUNCHER 1");
	}

	[Test]
	public void ForceStopApp_builds_an_am_force_stop_command()
	{
		var result = AdbCommandBuilder.Build(new AdbForceStopAppCommand(Serial, "com.example.app"));

		AssertShellCommand(result, "am force-stop 'com.example.app'");
	}

	[Test]
	public void OpenUri_builds_an_am_start_view_command()
	{
		var result = AdbCommandBuilder.Build(new AdbOpenUriCommand(Serial, "https://example.com/path"));

		AssertShellCommand(result, "am start -a android.intent.action.VIEW -d 'https://example.com/path'");
	}

	[Test]
	public void InputText_builds_a_quoted_input_text_command()
	{
		var result = AdbCommandBuilder.Build(new AdbInputTextCommand(Serial, "hello world"));

		AssertShellCommand(result, "input text 'hello world'");
	}

	[Test]
	public void InputText_quotes_an_embedded_single_quote_so_it_cannot_break_out()
	{
		var result = AdbCommandBuilder.Build(new AdbInputTextCommand(Serial, "it's a test; rm -rf /"));

		AssertShellCommand(result, @"input text 'it'\''s a test; rm -rf /'");
	}

	[Test]
	public void Tap_builds_an_input_tap_command()
	{
		var result = AdbCommandBuilder.Build(new AdbTapCommand(Serial, 100, 200));

		AssertShellCommand(result, "input tap 100 200");
	}

	[Test]
	public void Swipe_builds_an_input_swipe_command()
	{
		var result = AdbCommandBuilder.Build(new AdbSwipeCommand(Serial, 10, 20, 30, 40, 250));

		AssertShellCommand(result, "input swipe 10 20 30 40 250");
	}

	[Test]
	public void Reboot_normal_is_not_a_shell_command()
	{
		var result = AdbCommandBuilder.Build(new AdbRebootCommand(Serial, AdbRebootMode.Normal));

		AssertRawCommand(result, ["-s", Serial, "reboot"]);
	}

	[Test]
	public void Reboot_recovery_appends_the_recovery_argument()
	{
		var result = AdbCommandBuilder.Build(new AdbRebootCommand(Serial, AdbRebootMode.Recovery));

		AssertRawCommand(result, ["-s", Serial, "reboot", "recovery"]);
	}

	[Test]
	public void Reboot_bootloader_appends_the_bootloader_argument()
	{
		var result = AdbCommandBuilder.Build(new AdbRebootCommand(Serial, AdbRebootMode.Bootloader));

		AssertRawCommand(result, ["-s", Serial, "reboot", "bootloader"]);
	}

	[Test]
	public void Screenshot_is_not_a_shell_command()
	{
		var result = AdbCommandBuilder.Build(new AdbScreenshotCommand(Serial));

		AssertRawCommand(result, ["-s", Serial, "exec-out", "screencap -p"]);
	}

	[Test]
	public void An_injected_package_name_cannot_escape_the_quoting()
	{
		// The package validation regex admits only letters, digits, underscore and dots, so this
		// payload can never reach the shell template at all - the strongest possible defense against
		// escaping. InputText_quotes_an_embedded_single_quote_so_it_cannot_break_out above proves the
		// quoting itself is safe for values (like input text) that are allowed to carry arbitrary
		// characters.
		var result = AdbCommandBuilder.Build(new AdbStartAppCommand(Serial, "com.foo'; pm uninstall x"));

		Assert.That(result.Success, Is.False);
		Assert.That(result.Error, Is.EqualTo(AdbFailureCode.InvalidParameter));
	}

	private static void AssertShellCommand(
		Result<IReadOnlyList<string>, AdbFailureCode> result,
		string expectedShellCommand)
	{
		Assert.That(result.Success, Is.True);
		Assert.That(result.Data, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data, Has.Count.EqualTo(4));
			Assert.That(result.Data![0], Is.EqualTo("-s"));
			Assert.That(result.Data[1], Is.EqualTo(Serial));
			Assert.That(result.Data[2], Is.EqualTo("shell"));
			Assert.That(result.Data[3], Is.EqualTo(expectedShellCommand));
		});
	}

	private static void AssertRawCommand(Result<IReadOnlyList<string>, AdbFailureCode> result, string[] expectedArgv)
	{
		Assert.That(result.Success, Is.True);
		Assert.That(result.Data, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data![0], Is.EqualTo("-s"));
			Assert.That(result.Data[1], Is.EqualTo(Serial));
			Assert.That(result.Data, Does.Not.Contain("shell"));
			Assert.That(result.Data, Is.EqualTo(expectedArgv));
		});
	}

	[Test]
	public void PushFile_passes_both_paths_as_argv_entries()
	{
		var result = AdbCommandBuilder.Build(new AdbPushFileCommand(Serial,
			"/tmp/scratch",
			"/etc/macrodeck/kiosk-url"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data,
				Is.EqualTo(new[]
				{
					"-s", Serial, "push", "/tmp/scratch", "/etc/macrodeck/kiosk-url"
				}),
				"push takes paths as arguments, so nothing is handed to a device-side shell to re-parse");
		});
	}

	[TestCase("relative/path", Description = "a device path must be absolute")]
	[TestCase("/etc/../root/.ssh/authorized_keys", Description = "traversal must be refused, not normalised")]
	[TestCase("/etc/$(reboot)")]
	[TestCase("/etc/kiosk;reboot")]
	[TestCase("/etc/kiosk url")]
	public void PushFile_refuses_a_device_path_outside_the_closed_character_set(string devicePath)
	{
		var result = AdbCommandBuilder.Build(new AdbPushFileCommand(Serial,
			"/tmp/scratch",
			devicePath));

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public void PushFile_requires_a_local_path()
	{
		var result = AdbCommandBuilder.Build(new AdbPushFileCommand(Serial,
			"  ",
			"/etc/macrodeck/kiosk-url"));

		Assert.That(result.Success, Is.False);
	}

	[TestCase(AdbServiceManager.Supervisord, "supervisorctl restart 'chromium-kiosk'")]
	[TestCase(AdbServiceManager.Systemd, "systemctl restart 'chromium-kiosk'")]
	[TestCase(AdbServiceManager.SysVInit, "/etc/init.d/'chromium-kiosk' restart")]
	public void RestartService_uses_a_fixed_verb_per_init_system(AdbServiceManager manager, string expected)
	{
		var result = AdbCommandBuilder.Build(new AdbRestartServiceCommand(Serial,
			manager,
			"chromium-kiosk"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data, Is.EqualTo(new[] { "-s", Serial, "shell", expected }));
		});
	}

	[TestCase("kiosk; reboot")]
	[TestCase("$(reboot)")]
	[TestCase("../../bin/sh")]
	[TestCase("")]
	public void RestartService_refuses_a_service_name_that_could_carry_a_second_command(string serviceName)
	{
		var result = AdbCommandBuilder.Build(new AdbRestartServiceCommand(Serial,
			AdbServiceManager.Systemd,
			serviceName));

		Assert.That(result.Success, Is.False);
	}

	[Test]
	public void RemountRootWritable_is_a_fixed_command_with_nothing_to_inject_into()
	{
		var result = AdbCommandBuilder.Build(new AdbRemountRootWritableCommand(Serial));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data,
				Is.EqualTo(new[]
				{
					"-s", Serial, "shell", "mount -o remount,rw /"
				}),
				"appliance firmwares ship the root filesystem read-only, so a write needs this first");
		});
	}
}
