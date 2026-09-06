using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbPropertyParsersTests
{
	[Test]
	public void ParseBatteryLevel_reads_the_level_marker()
	{
		const string output =
			"Current Battery Service state:\n" +
			"  AC powered: false\n" +
			"  USB powered: true\n" +
			"  status: 2\n" +
			"  health: 2\n" +
			"  present: true\n" +
			"  level: 85\n" +
			"  scale: 100\n" +
			"  voltage: 4123\n" +
			"  temperature: 320\n" +
			"  technology: Li-ion\n";

		Assert.That(AdbPropertyParsers.ParseBatteryLevel(output), Is.EqualTo(85));
	}

	[Test]
	public void ParseBatteryLevel_reads_a_terser_OEM_shape()
	{
		const string output =
			"Current Battery Service state:\n" +
			"  USB powered: false\n" +
			"  level: 42\n" +
			"  scale: 100\n";

		Assert.That(AdbPropertyParsers.ParseBatteryLevel(output), Is.EqualTo(42));
	}

	[Test]
	public void ParseBatteryLevel_returns_null_when_the_marker_is_absent()
	{
		const string output = "Current Battery Service state:\n  AC powered: false\n";

		Assert.That(AdbPropertyParsers.ParseBatteryLevel(output), Is.Null);
	}

	[Test]
	public void ParseBatteryLevel_returns_null_when_the_value_is_outside_0_to_100()
	{
		const string output = "Current Battery Service state:\n  level: 150\n";

		Assert.That(AdbPropertyParsers.ParseBatteryLevel(output), Is.Null);
	}

	[Test]
	public void ParseScreenOn_reads_mWakefulness_Awake_as_true()
	{
		const string output = "Power Manager State:\n  mWakefulness=Awake\n  mIsPowered=true\n";

		Assert.That(AdbPropertyParsers.ParseScreenOn(output), Is.True);
	}

	[Test]
	public void ParseScreenOn_reads_mWakefulness_Asleep_as_false()
	{
		const string output = "Power Manager State:\n  mWakefulness=Asleep\n";

		Assert.That(AdbPropertyParsers.ParseScreenOn(output), Is.False);
	}

	[Test]
	public void ParseScreenOn_reads_mWakefulness_Dozing_as_false()
	{
		const string output = "Power Manager State:\n  mWakefulness=Dozing\n";

		Assert.That(AdbPropertyParsers.ParseScreenOn(output), Is.False);
	}

	[Test]
	public void ParseScreenOn_falls_back_to_Display_Power_state_ON()
	{
		const string output = "Display Power: state=ON\n  mScreenState=ON\n";

		Assert.That(AdbPropertyParsers.ParseScreenOn(output), Is.True);
	}

	[Test]
	public void ParseScreenOn_falls_back_to_Display_Power_state_OFF()
	{
		const string output = "Display Power: state=OFF\n";

		Assert.That(AdbPropertyParsers.ParseScreenOn(output), Is.False);
	}

	[Test]
	public void ParseScreenOn_returns_null_when_no_marker_is_present()
	{
		const string output = "Power Manager State:\n  mSomethingElse=true\n";

		Assert.That(AdbPropertyParsers.ParseScreenOn(output), Is.Null);
	}

	[Test]
	public void ParseLocked_reads_mDreamingLockscreen_true()
	{
		const string output =
			"WINDOW MANAGER LOCK SCREEN STATE\n" +
			"  mDreamingLockscreen=true\n" +
			"  mCurrentFocus=Window{38a38a3 u0 com.example.app/com.example.app.MainActivity}\n";

		Assert.That(AdbPropertyParsers.ParseLocked(output), Is.True);
	}

	[Test]
	public void ParseLocked_reads_mDreamingLockscreen_false()
	{
		const string output =
			"  mDreamingLockscreen=false\n" +
			"  mCurrentFocus=Window{a1b2c3d u0 com.android.launcher3/com.android.launcher3.Launcher}\n";

		Assert.That(AdbPropertyParsers.ParseLocked(output), Is.False);
	}

	[Test]
	public void ParseLocked_returns_null_when_the_marker_is_absent()
	{
		const string output = "  mSomeOtherField=true\n";

		Assert.That(AdbPropertyParsers.ParseLocked(output), Is.Null);
	}

	[Test]
	public void ParseForegroundPackage_reads_the_package_before_the_slash()
	{
		const string output =
			"  mDreamingLockscreen=true\n" +
			"  mCurrentFocus=Window{38a38a3 u0 com.example.app/com.example.app.MainActivity}\n";

		Assert.That(AdbPropertyParsers.ParseForegroundPackage(output), Is.EqualTo("com.example.app"));
	}

	[Test]
	public void ParseForegroundPackage_reads_a_launcher_shape_from_a_different_Android_version()
	{
		const string output =
			"  mCurrentFocus=Window{a1b2c3d u0 com.android.launcher3/com.android.launcher3.Launcher}\n";

		Assert.That(AdbPropertyParsers.ParseForegroundPackage(output), Is.EqualTo("com.android.launcher3"));
	}

	[Test]
	public void ParseForegroundPackage_returns_null_for_mCurrentFocus_null()
	{
		const string output = "  mDreamingLockscreen=false\n  mCurrentFocus=null\n";

		Assert.That(AdbPropertyParsers.ParseForegroundPackage(output), Is.Null);
	}

	[Test]
	public void ParseForegroundPackage_returns_null_when_the_marker_is_absent()
	{
		const string output = "  mSomeOtherField=true\n";

		Assert.That(AdbPropertyParsers.ParseForegroundPackage(output), Is.Null);
	}

	[TestCase("Samsung\n", "Samsung")]
	[TestCase("  Google  \n", "Google")]
	[TestCase("OnePlus\nignored-second-line\n", "OnePlus")]
	public void ParseSingleLineProperty_trims_and_returns_the_first_non_empty_line(string output, string expected)
	{
		Assert.That(AdbPropertyParsers.ParseSingleLineProperty(output), Is.EqualTo(expected));
	}

	[TestCase("")]
	[TestCase("\n")]
	[TestCase("   \n  \n")]
	public void ParseSingleLineProperty_returns_null_when_there_is_no_content(string output)
	{
		Assert.That(AdbPropertyParsers.ParseSingleLineProperty(output), Is.Null);
	}
}
