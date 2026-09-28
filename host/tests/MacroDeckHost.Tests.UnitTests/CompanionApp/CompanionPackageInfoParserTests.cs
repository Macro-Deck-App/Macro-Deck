using MacroDeckHost.CompanionApp;

namespace MacroDeckHost.Tests.UnitTests.CompanionApp;

public class CompanionPackageInfoParserTests
{
	private const string Package = "app.macrodeck.companion";

	internal static string Installed(int versionCode, string versionName, string installer = "null", bool userZeroInstalled = true)
		=> $$"""
			Activity Resolver Table:
			  Non-Data Actions:
			      android.intent.action.MAIN:
			        5f1 app.macrodeck.companion/.MainActivity filter 9a2

			Packages:
			  Package [app.macrodeck.companion] (c0ffee):
			    appId=10234
			    pkg=Package{d4 app.macrodeck.companion}
			    versionCode={{versionCode}} minSdk=23 targetSdk=35
			    versionName={{versionName}}
			    installerPackageName={{installer}}
			    User 0: ceDataInode=12345 installed={{(userZeroInstalled ? "true" : "false")}} hidden=false stopped=false

			Queries:
			  system apps queryable: false
			""";

	[Test]
	public void A_sideloaded_install_reports_its_version_and_no_installer()
	{
		var info = CompanionPackageInfoParser.ParsePackageInfo(Installed(8, "26.0.0"), Package);

		Assert.That(info, Is.EqualTo(new CompanionPackageInfo(8, "26.0.0", null)));
		Assert.That(info!.FromPlayStore, Is.False);
	}

	[Test]
	public void A_google_play_install_is_recognised_by_its_installer()
	{
		var info = CompanionPackageInfoParser.ParsePackageInfo(Installed(9, "26.1.0", "com.android.vending"), Package);

		Assert.That(info!.FromPlayStore, Is.True);
	}

	[Test]
	public void Android_6_output_with_carriage_returns_is_read()
	{
		var output = "Packages:\r\n  Package [app.macrodeck.companion] (3f):\r\n    userId=10080\r\n" +
			"    versionCode=7 targetSdk=23\r\n    versionName=25.9.0\r\n    installerPackageName=null\r\n";

		Assert.That(CompanionPackageInfoParser.ParsePackageInfo(output, Package),
			Is.EqualTo(new CompanionPackageInfo(7, "25.9.0", null)));
	}

	[TestCase("Unable to find package: app.macrodeck.companion\n")]
	[TestCase("Dexopt state:\n  [com.android.settings]\n    arm64: [status=speed-profile]\n")]
	[TestCase("")]
	public void Output_without_the_package_means_it_is_not_installed(string output)
	{
		Assert.That(CompanionPackageInfoParser.ParsePackageInfo(output, Package), Is.Null);
	}

	[Test]
	public void An_install_for_another_user_only_is_not_installed_for_the_device_owner()
	{
		Assert.That(CompanionPackageInfoParser.ParsePackageInfo(Installed(9, "26.1.0", userZeroInstalled: false), Package),
			Is.Null);
	}

	[Test]
	public void The_hidden_factory_copy_of_an_updated_system_app_does_not_count()
	{
		var output = Installed(9, "26.1.0") +
			"\nHidden system packages:\n  Package [app.macrodeck.companion] (b1):\n    versionCode=1 targetSdk=35\n    versionName=1.0.0\n";

		Assert.That(CompanionPackageInfoParser.ParsePackageInfo(output, Package)!.VersionCode, Is.EqualTo(9));
	}

	[TestCase("23\r\n", 23)]
	[TestCase("35\n", 35)]
	public void The_sdk_level_is_read_from_getprop(string output, int expected)
	{
		Assert.That(CompanionPackageInfoParser.ParseSdkLevel(output), Is.EqualTo(expected));
	}

	[TestCase("")]
	[TestCase("/bin/sh: getprop: not found\n")]
	[TestCase("0")]
	public void Anything_but_a_positive_level_is_not_android(string output)
	{
		Assert.That(CompanionPackageInfoParser.ParseSdkLevel(output), Is.Null);
	}
}
