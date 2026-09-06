using MacroDeck.Plugin.Cli.Runtime;
using MacroDeck.Plugin.Hosting;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="PluginEnvironmentComposer.Compose" /> as a pure function. Every expected variable name
/// below is copied from <c>docs/src/content/docs/guides/hosting.md</c>'s "What the supervisor injects"
/// table, not from <see cref="PluginEnvironmentComposer" />'s own source - the whole point is to catch the
/// composer disagreeing with the documented contract, which reading the composer's code back to itself
/// could never do.
/// </summary>
[TestFixture]
public class PluginEnvironmentComposerTests
{
	private static readonly string[] _managedVariables =
	[
		"MACRO_DECK_PLUGIN_MODE",
		"MACRO_DECK_PLUGIN_HOST_URL",
		"MACRO_DECK_PLUGIN_ID",
		"MACRO_DECK_PLUGIN_SECRET",
		"MACRO_DECK_PLUGIN_DATA_DIRECTORY",
		"MACRO_DECK_PLUGIN_INSTANCE_ID",
		"MACRO_DECK_PLUGIN_LAUNCH_ID",
		"ASPNETCORE_URLS"
	];

	private static readonly string[] _selfRegisteringVariables =
	[
		"MACRO_DECK_PLUGIN_MODE",
		"MACRO_DECK_PLUGIN_HOST_URL",
		"MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN",
		"MACRO_DECK_PLUGIN_STATE_DIRECTORY",
		"MACRO_DECK_PLUGIN_INSTANCE_ID",
		"MACRO_DECK_PLUGIN_PAIRING",
		"ASPNETCORE_URLS"
	];

	private static PluginEnvironmentRequest ManagedRequest() => new()
	{
		Mode = PluginRegistrationMode.Managed,
		HostUrl = "http://127.0.0.1:8193",
		InstanceId = "instance-1",
		ListenUrl = "http://127.0.0.1:54321",
		PluginId = "com.example.test-plugin",
		Secret = "s3cr3t",
		DataDirectory = "/data",
		LaunchId = "launch-1",
		// Populated too, to prove a field the chosen mode does not use is simply ignored, not merely
		// coincidentally absent because the test never set it.
		EnrollmentToken = "should-not-appear",
		StateDirectory = "/should-not-appear"
	};

	private static PluginEnvironmentRequest SelfRegisteringRequest() => new()
	{
		Mode = PluginRegistrationMode.SelfRegistering,
		HostUrl = "http://127.0.0.1:8193",
		InstanceId = "instance-1",
		ListenUrl = "http://127.0.0.1:54321",
		EnrollmentToken = "a-real-token",
		StateDirectory = "/state",
		// Same reasoning as above, for the managed-only fields.
		PluginId = "should-not-appear",
		Secret = "should-not-appear",
		DataDirectory = "/should-not-appear",
		LaunchId = "should-not-appear"
	};

	[Test]
	public void Managed_mode_sets_exactly_the_documented_managed_variables()
	{
		var environment = PluginEnvironmentComposer.Compose(ManagedRequest());

		Assert.Multiple(() =>
		{
			Assert.That(environment.Keys, Is.EquivalentTo(_managedVariables));
			Assert.That(environment["MACRO_DECK_PLUGIN_MODE"], Is.EqualTo("Managed"));
			Assert.That(environment["MACRO_DECK_PLUGIN_HOST_URL"], Is.EqualTo("http://127.0.0.1:8193"));
			Assert.That(environment["MACRO_DECK_PLUGIN_ID"], Is.EqualTo("com.example.test-plugin"));
			Assert.That(environment["MACRO_DECK_PLUGIN_SECRET"], Is.EqualTo("s3cr3t"));
			Assert.That(environment["MACRO_DECK_PLUGIN_DATA_DIRECTORY"], Is.EqualTo("/data"));
			Assert.That(environment["MACRO_DECK_PLUGIN_INSTANCE_ID"], Is.EqualTo("instance-1"));
			Assert.That(environment["MACRO_DECK_PLUGIN_LAUNCH_ID"], Is.EqualTo("launch-1"));
			Assert.That(environment["ASPNETCORE_URLS"], Is.EqualTo("http://127.0.0.1:54321"));
		});
	}

	[Test]
	public void Managed_mode_never_sets_a_self_registering_only_variable()
	{
		var environment = PluginEnvironmentComposer.Compose(ManagedRequest());

		Assert.Multiple(() =>
		{
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN"), Is.False);
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_STATE_DIRECTORY"), Is.False);
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_PAIRING"), Is.False);
		});
	}

	[Test]
	public void Self_registering_mode_sets_exactly_the_documented_self_registering_variables()
	{
		var environment = PluginEnvironmentComposer.Compose(SelfRegisteringRequest());

		Assert.Multiple(() =>
		{
			Assert.That(environment.Keys, Is.EquivalentTo(_selfRegisteringVariables));
			Assert.That(environment["MACRO_DECK_PLUGIN_MODE"], Is.EqualTo("SelfRegistering"));
			Assert.That(environment["MACRO_DECK_PLUGIN_HOST_URL"], Is.EqualTo("http://127.0.0.1:8193"));
			Assert.That(environment["MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN"], Is.EqualTo("a-real-token"));
			Assert.That(environment["MACRO_DECK_PLUGIN_STATE_DIRECTORY"], Is.EqualTo("/state"));
			Assert.That(environment["MACRO_DECK_PLUGIN_INSTANCE_ID"], Is.EqualTo("instance-1"));
			Assert.That(environment["MACRO_DECK_PLUGIN_PAIRING"], Is.EqualTo("true"));
			Assert.That(environment["ASPNETCORE_URLS"], Is.EqualTo("http://127.0.0.1:54321"));
		});
	}

	[Test]
	public void Self_registering_mode_never_sets_a_managed_only_variable()
	{
		var environment = PluginEnvironmentComposer.Compose(SelfRegisteringRequest());

		Assert.Multiple(() =>
		{
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_ID"), Is.False);
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_SECRET"), Is.False);
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_DATA_DIRECTORY"), Is.False);
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_LAUNCH_ID"), Is.False);
		});
	}

	[Test]
	public void Every_inherited_macro_deck_plugin_and_aspnetcore_urls_variable_is_scrubbed()
	{
		// Simulates macrodeck-plugin run itself having been launched from inside another plugin's own
		// environment - stale values a fresh launch must never leak into the child it starts.
		var inherited = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["MACRO_DECK_PLUGIN_MODE"] = "SelfRegistering",
			["MACRO_DECK_PLUGIN_HOST_URL"] = "http://stale-host",
			["MACRO_DECK_PLUGIN_ID"] = "com.stale.plugin",
			["MACRO_DECK_PLUGIN_SECRET"] = "stale-secret",
			["MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN"] = "stale-token",
			["MACRO_DECK_PLUGIN_INSTANCE_ID"] = "stale-instance",
			["MACRO_DECK_PLUGIN_STATE_DIRECTORY"] = "/stale-state",
			["MACRO_DECK_PLUGIN_DATA_DIRECTORY"] = "/stale-data",
			["MACRO_DECK_PLUGIN_LAUNCH_ID"] = "stale-launch",
			["ASPNETCORE_URLS"] = "http://stale-listener",
			// Not part of the scrubbed vocabulary - must pass through untouched.
			["PATH"] = "/usr/bin:/bin",
			["MACRO_DECK_PLUGIN_UNKNOWN_FUTURE_VARIABLE"] = "still scrubbed by prefix alone"
		};

		var environment = PluginEnvironmentComposer.Compose(ManagedRequest(), inherited);

		Assert.Multiple(() =>
		{
			// The fresh values win, not the stale inherited ones.
			Assert.That(environment["MACRO_DECK_PLUGIN_HOST_URL"], Is.EqualTo("http://127.0.0.1:8193"));
			Assert.That(environment["MACRO_DECK_PLUGIN_ID"], Is.EqualTo("com.example.test-plugin"));
			Assert.That(environment["ASPNETCORE_URLS"], Is.EqualTo("http://127.0.0.1:54321"));

			// A self-registering-only variable inherited from the stale environment must not survive into
			// a managed launch.
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_ENROLLMENT_TOKEN"), Is.False);
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_STATE_DIRECTORY"), Is.False);

			// A variable this table has never heard of is still scrubbed by prefix alone.
			Assert.That(environment.ContainsKey("MACRO_DECK_PLUGIN_UNKNOWN_FUTURE_VARIABLE"), Is.False);

			// Untouched: not part of the scrubbed vocabulary.
			Assert.That(environment["PATH"], Is.EqualTo("/usr/bin:/bin"));
		});
	}
}
