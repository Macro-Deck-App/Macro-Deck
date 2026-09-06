using MacroDeck.Plugin.Hosting.Configuration;
using MacroDeck.Plugin.Hosting.Credentials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RegistrationModeTests
{
	private static PluginRegistrationMode ModeOf(params (string Key, string Value)[] configuration)
	{
		var builder = MacroDeckPlugin.CreatePlugin();

		foreach (var (key, value) in configuration)
		{
			builder.Configuration[key] = value;
		}

		using var plugin = builder.Build();
		return plugin.Services.GetRequiredService<PluginRegistrationModeAccessor>().Mode;
	}

	[Test]
	public void Every_documented_environment_variable_maps_to_its_configuration_key()
	{
		var values = PluginEnvironmentConfiguration.Read(variable => variable + "-value");

		Assert.Multiple(() =>
		{
			foreach (var (variable, key) in PluginEnvironmentConfiguration.KeysByVariable)
			{
				Assert.That(variable, Does.StartWith(PluginEnvironmentConfiguration.Prefix));
				Assert.That(values[key], Is.EqualTo(variable + "-value"));
			}
		});
	}

	[Test]
	public void An_unset_variable_contributes_nothing()
	{
		Assert.That(PluginEnvironmentConfiguration.Read(_ => null), Is.Empty);
	}

	[Test]
	public void An_id_and_a_secret_together_mean_the_host_launched_this_plugin()
	{
		Assert.That(ModeOf(("MacroDeck:Plugin:Id", "com.example.test"), ("MacroDeck:Plugin:Secret", "s")),
			Is.EqualTo(PluginRegistrationMode.Managed));
	}

	[Test]
	public void Without_injected_credentials_a_plugin_registers_itself()
	{
		Assert.That(ModeOf(), Is.EqualTo(PluginRegistrationMode.SelfRegistering));
	}

	[Test]
	public void A_secret_without_an_id_is_not_enough_to_look_managed()
	{
		Assert.That(ModeOf(("MacroDeck:Plugin:Secret", "s")), Is.EqualTo(PluginRegistrationMode.SelfRegistering));
	}

	[Test]
	public void Configuration_overrides_what_is_inferred()
	{
		Assert.That(ModeOf(("MacroDeck:Plugin:Mode", "Managed")), Is.EqualTo(PluginRegistrationMode.Managed));
	}

	[Test]
	public void An_explicit_UseRegistrationMode_wins_over_configuration()
	{
		var builder = MacroDeckPlugin.CreatePlugin()
			.UseRegistrationMode(PluginRegistrationMode.SelfRegistering);

		builder.Configuration["MacroDeck:Plugin:Mode"] = "Managed";

		using var plugin = builder.Build();

		Assert.That(plugin.Services.GetRequiredService<PluginRegistrationModeAccessor>().Mode,
			Is.EqualTo(PluginRegistrationMode.SelfRegistering));
	}

	[Test]
	public async Task Managed_credentials_come_from_configuration_and_are_never_persisted()
	{
		var store = new EnvironmentPluginCredentialStore(Options.Create(new PluginHostOptions
		{
			Id = "com.example.test",
			Secret = "secret",
			HostUrl = "http://127.0.0.1:8193"
		}));

		var credentials = await store.LoadAsync();

		Assert.Multiple(() =>
		{
			Assert.That(credentials!.PluginId, Is.EqualTo("com.example.test"));
			Assert.That(credentials.Secret, Is.EqualTo("secret"));
			Assert.That(store.CanSave, Is.False);
		});

		Assert.ThrowsAsync<NotSupportedException>(() => store.SaveAsync(credentials!));
	}

	[Test]
	public async Task Managed_mode_reports_no_credentials_when_the_supervisor_injected_none()
	{
		var store = new EnvironmentPluginCredentialStore(Options.Create(new PluginHostOptions()));

		Assert.That(await store.LoadAsync(), Is.Null);
	}
}

[TestFixture]
public class FilePluginCredentialStoreTests
{
	private string _directory = string.Empty;

	[SetUp]
	public void SetUp()
		=> _directory = Directory.CreateTempSubdirectory("macro-deck-plugin-tests").FullName;

	[TearDown]
	public void TearDown() => Directory.Delete(_directory, recursive: true);

	private FilePluginCredentialStore Store()
		=> new(Options.Create(new PluginHostOptions { StateDirectory = _directory }),
			new PluginMetadata { Id = "com.example.test", Name = "Test", Version = "1.0.0" },
			Serilog.Core.Logger.None);

	[Test]
	public async Task Nothing_is_stored_before_the_plugin_has_registered()
	{
		Assert.That(await Store().LoadAsync(), Is.Null);
	}

	[Test]
	public async Task Stored_credentials_round_trip()
	{
		var store = Store();
		await store.SaveAsync(new PluginCredentials("com.example.test", "http://127.0.0.1:8193", "secret"));

		var loaded = await Store().LoadAsync();

		Assert.Multiple(() =>
		{
			Assert.That(loaded!.PluginId, Is.EqualTo("com.example.test"));
			Assert.That(loaded.Secret, Is.EqualTo("secret"));
			Assert.That(loaded.HostUrl, Is.EqualTo("http://127.0.0.1:8193"));
		});
	}

	[Test]
	public async Task Saving_twice_replaces_the_stored_credentials_and_leaves_no_temporary_file()
	{
		var store = Store();
		await store.SaveAsync(new PluginCredentials("com.example.test", "http://host", "first"));
		await store.SaveAsync(new PluginCredentials("com.example.test", "http://host", "second"));

		Assert.Multiple(() =>
		{
			Assert.That(Store().LoadAsync().Result!.Secret, Is.EqualTo("second"));
			Assert.That(Directory.GetFiles(store.Directory), Has.Length.EqualTo(1));
		});
	}

	[Test]
	public async Task An_unreadable_credential_file_is_treated_as_absent()
	{
		var store = Store();
		await store.SaveAsync(new PluginCredentials("com.example.test", "http://host", "secret"));
		await File.WriteAllTextAsync(Path.Combine(store.Directory, "credentials.json"), "{ not json");

		// Registering again beats a plugin that can never start; the host refuses a duplicate anyway.
		Assert.That(await Store().LoadAsync(), Is.Null);
	}

	// No [Platform] attribute: NUnit's "Unix" does not match macOS, so the attribute would have made
	// this quietly skip on two of the three platforms it is meant to cover. The runtime guard below is
	// the whole of the platform gating.
	[Test]
	public async Task The_credential_file_and_its_directory_are_owner_only()
	{
		var store = Store();
		await store.SaveAsync(new PluginCredentials("com.example.test", "http://host", "secret"));

		// Windows has no file mode; the per-user profile ACL is the protection there instead.
		if (OperatingSystem.IsWindows())
		{
			Assert.Ignore("Unix file modes only.");
			return;
		}

		var file = File.GetUnixFileMode(Path.Combine(store.Directory, "credentials.json"));
		var directory = File.GetUnixFileMode(store.Directory);

		Assert.Multiple(() =>
		{
			Assert.That(file, Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite));
			Assert.That(directory,
				Is.EqualTo(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute));
		});
	}
}
