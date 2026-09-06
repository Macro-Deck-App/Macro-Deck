using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbExecutableLocatorTests
{
	[Test]
	public void Windows_well_known_localappdata_path_is_found()
	{
		var expected = Path.Combine("fake-localappdata", "Android", "Sdk", "platform-tools", "adb.exe");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?> { ["LOCALAPPDATA"] = "fake-localappdata" },
			isWindows: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void Windows_well_known_programfiles_path_is_found_when_localappdata_candidate_is_missing()
	{
		var expected = Path.Combine("fake-programfiles", "Android", "platform-tools", "adb.exe");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?>
			{
				["LOCALAPPDATA"] = "fake-localappdata",
				["PROGRAMFILES"] = "fake-programfiles"
			},
			isWindows: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void MacOs_well_known_home_library_path_is_found()
	{
		var expected = Path.Combine("fake-home", "Library", "Android", "sdk", "platform-tools", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?> { ["HOME"] = "fake-home" },
			isMacOs: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void MacOs_well_known_homebrew_path_is_found_when_home_is_unset()
	{
		var expected = Path.Combine("/opt", "homebrew", "bin", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?>(),
			isMacOs: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void MacOs_well_known_usr_local_bin_path_is_found_when_earlier_candidates_are_missing()
	{
		var expected = Path.Combine("/usr", "local", "bin", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?>(),
			isMacOs: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void Linux_well_known_home_android_sdk_path_is_found()
	{
		var expected = Path.Combine("fake-home", "Android", "Sdk", "platform-tools", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?> { ["HOME"] = "fake-home" },
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void Linux_well_known_usr_lib_android_sdk_path_is_found()
	{
		var expected = Path.Combine("/usr", "lib", "android-sdk", "platform-tools", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?>(),
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void Linux_well_known_usr_local_bin_path_is_found()
	{
		var expected = Path.Combine("/usr", "local", "bin", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?>(),
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void Linux_well_known_usr_bin_path_is_found()
	{
		var expected = Path.Combine("/usr", "bin", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [expected],
			environment: new Dictionary<string, string?>(),
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(expected));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.WellKnownSdkLocation));
		});
	}

	[Test]
	public void The_configured_path_wins_over_everything_else()
	{
		const string configured = "fake-configured-adb";
		var wellKnown = Path.Combine("/usr", "bin", "adb");

		var result = Resolve(configuredPath: configured,
			existingPaths: [configured, wellKnown],
			environment: new Dictionary<string, string?> { ["ANDROID_HOME"] = "fake-android-home" },
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(configured));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.Configured));
		});
	}

	[Test]
	public void A_configured_path_that_does_not_exist_does_not_short_circuit_to_a_bogus_result()
	{
		const string configured = "fake-configured-adb-that-does-not-exist";
		var androidHomeCandidate = Path.Combine("fake-android-home", "platform-tools", "adb");

		var result = Resolve(configuredPath: configured,
			existingPaths: [androidHomeCandidate],
			environment: new Dictionary<string, string?> { ["ANDROID_HOME"] = "fake-android-home" },
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(androidHomeCandidate));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.AndroidSdkEnvironment));
		});
	}

	[Test]
	public void ANDROID_HOME_beats_the_well_known_location()
	{
		var androidHomeCandidate = Path.Combine("fake-android-home", "platform-tools", "adb");
		var wellKnown = Path.Combine("/usr", "bin", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [androidHomeCandidate, wellKnown],
			environment: new Dictionary<string, string?> { ["ANDROID_HOME"] = "fake-android-home" },
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(androidHomeCandidate));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.AndroidSdkEnvironment));
		});
	}

	[Test]
	public void ANDROID_SDK_ROOT_is_used_when_ANDROID_HOME_is_unset()
	{
		var sdkRootCandidate = Path.Combine("fake-sdk-root", "platform-tools", "adb");

		var result = Resolve(configuredPath: null,
			existingPaths: [sdkRootCandidate],
			environment: new Dictionary<string, string?> { ["ANDROID_SDK_ROOT"] = "fake-sdk-root" },
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(sdkRootCandidate));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.AndroidSdkEnvironment));
		});
	}

	[Test]
	public void PATH_lookup_finds_a_bare_executable_and_yields_Source_Path()
	{
		const string directory1 = "fake-dir-one";
		const string directory2 = "fake-dir-two";
		var candidate = Path.Combine(directory2, "adb");
		var pathValue = string.Join(Path.PathSeparator, directory1, directory2);

		var result = Resolve(configuredPath: null,
			existingPaths: [candidate],
			environment: new Dictionary<string, string?> { ["PATH"] = pathValue },
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.EqualTo(candidate));
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.Path));
		});
	}

	[Test]
	public void Nothing_found_yields_a_null_path_and_source_None()
	{
		var result = Resolve(configuredPath: null,
			existingPaths: [],
			environment: new Dictionary<string, string?>(),
			isLinux: true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Path, Is.Null);
			Assert.That(result.Source, Is.EqualTo(AdbExecutableSource.None));
		});
	}

	[Test]
	public void The_executable_name_is_adb_exe_on_Windows_and_plain_adb_on_macOS_and_Linux()
	{
		var windowsCandidate = Path.Combine("fake-dir", "adb.exe");
		var otherCandidate = Path.Combine("fake-dir", "adb");
		var environment = new Dictionary<string, string?> { ["PATH"] = "fake-dir" };

		Assert.Multiple(() =>
		{
			Assert.That(Resolve(null, [windowsCandidate], environment, isWindows: true).Path,
				Is.EqualTo(windowsCandidate));
			Assert.That(Resolve(null, [otherCandidate], environment, isMacOs: true).Path,
				Is.EqualTo(otherCandidate));
			Assert.That(Resolve(null, [otherCandidate], environment, isLinux: true).Path,
				Is.EqualTo(otherCandidate));
		});
	}

	private static (string? Path, AdbExecutableSource Source) Resolve(
		string? configuredPath,
		IEnumerable<string> existingPaths,
		Dictionary<string, string?> environment,
		bool isWindows = false,
		bool isMacOs = false,
		bool isLinux = false)
	{
		var existing = new HashSet<string>(existingPaths, StringComparer.Ordinal);

		return AdbExecutableLocator.Resolve(configuredPath,
			existing.Contains,
			key => environment.TryGetValue(key, out var value) ? value : null,
			isWindows,
			isMacOs,
			isLinux);
	}
}
