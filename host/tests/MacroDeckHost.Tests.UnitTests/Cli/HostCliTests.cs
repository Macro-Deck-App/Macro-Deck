using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Cli;

namespace MacroDeckHost.Tests.UnitTests.Cli;

[TestFixture]
internal sealed class HostCliTests
{
	private const string Executable = "plugin-entry";

	private string _root = null!;

	private string PluginsDirectory => Path.Combine(_root, "plugins");

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-cli-" + Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, true);
		}
	}

	[Test]
	public void Version_prints_the_installed_version()
	{
		var (exitCode, output) = Run(HostCommand.Version);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(HostCli.Success));
			Assert.That(output, Does.StartWith("Macro Deck " + HostVersion.Current));
		});
	}

	[Test]
	public void Help_lists_every_option()
	{
		var (exitCode, output) = Run(HostCommand.Help);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(HostCli.Success));
			Assert.That(output, Does.Contain("--help").And.Contain("--version"));
			Assert.That(output, Does.Contain("--list-plugins").And.Contain("--disable-plugins"));
			Assert.That(output, Does.Contain("--help, -h").And.Contain("--quit"));
		});
	}

	[Test]
	public void Listing_without_a_data_folder_reports_no_plugins_and_creates_nothing()
	{
		var (exitCode, output) = Run(HostCommand.ListPlugins);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(HostCli.Success));
			Assert.That(output, Does.Contain("No plugins are installed."));
			Assert.That(Directory.Exists(_root), Is.False);
		});
	}

	[Test]
	public void Broken_plugins_are_reported_without_hiding_the_healthy_ones()
	{
		InstallPlugin("com.acme.good", "1.2.0", Manifest("com.acme.good", "Good Plugin", "1.2.0"));
		WriteVersion("com.acme.good", "1.1.0", Manifest("com.acme.good", "Good Plugin", "1.1.0"));
		InstallPlugin("com.acme.corrupt", "2.0.0", "{ not json");
		WriteVersion("com.acme.nopointer", "1.0.0", Manifest("com.acme.nopointer", "No Pointer", "1.0.0"));
		InstallPlugin("com.acme.dangling", "9.9.9", null);
		WriteVersion("com.acme.dangling", "1.0.0", null);
		Directory.CreateDirectory(Path.Combine(PluginsDirectory, "Not A Plugin"));

		var (exitCode, output) = Run(HostCommand.ListPlugins);

		Assert.Multiple(() =>
		{
			Assert.That(exitCode, Is.EqualTo(HostCli.Success));
			Assert.That(Section(output, "com.acme.good"),
				Does.Contain("Name: Good Plugin")
					.And.Contain("Active version: 1.2.0")
					.And.Contain("Other installed versions: 1.1.0"));
			Assert.That(Section(output, "com.acme.corrupt"),
				Does.Contain("Active version: 2.0.0")
					.And.Contain("The manifest of version 2.0.0 is invalid (Malformed)."));
			Assert.That(Section(output, "com.acme.nopointer"),
				Does.Contain("No usable active version.").And.Contain("Other installed versions: 1.0.0"));
			Assert.That(Section(output, "com.acme.dangling"),
				Does.Contain("No usable active version.").And.Contain("Version 1.0.0 has no manifest."));
			Assert.That(output, Does.Contain("Not A Plugin: not a valid plugin folder."));
		});
	}

	[Test]
	[Platform(Exclude = "Win", Reason = "Unix file modes")]
	[UnsupportedOSPlatform("windows")]
	public void A_plugin_folder_that_cannot_be_read_does_not_hide_the_others()
	{
		InstallPlugin("com.acme.good", "1.0.0", Manifest("com.acme.good", "Good Plugin", "1.0.0"));
		var locked = Path.Combine(PluginsDirectory, "com.acme.locked", "versions");
		Directory.CreateDirectory(locked);
		File.SetUnixFileMode(locked, UnixFileMode.None);
		try
		{
			if (CanList(locked))
			{
				Assert.Ignore("This account can read any folder, so access cannot be denied here.");
			}

			var (exitCode, output) = Run(HostCommand.ListPlugins);

			Assert.Multiple(() =>
			{
				Assert.That(exitCode, Is.EqualTo(HostCli.Success));
				Assert.That(output, Does.Contain("com.acme.locked: not a valid plugin folder."));
				Assert.That(Section(output, "com.acme.good"), Does.Contain("Name: Good Plugin"));
			});
		}
		finally
		{
			File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}

	[Test]
	public void Listing_reads_manifests_without_running_or_changing_anything()
	{
		InstallPlugin("com.acme.good", "1.0.0", Manifest("com.acme.good", "Good Plugin", "1.0.0"));
		var before = Snapshot();

		Run(HostCommand.ListPlugins);

		Assert.That(Snapshot(), Is.EqualTo(before));
	}

	[Test]
	public void Output_in_another_language_is_written_as_utf8()
	{
		using var stream = new MemoryStream();
		using (var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true))
		{
			new HostCli(writer, "ru").Run(HostCommand.ListPlugins, DataRoot());
		}

		Assert.That(Encoding.UTF8.GetString(stream.ToArray()), Does.Contain("Плагины не установлены."));
	}

	private (int ExitCode, string Output) Run(HostCommand command)
	{
		using var writer = new StringWriter(CultureInfo.InvariantCulture);
		var exitCode = new HostCli(writer, "en").Run(command, DataRoot());
		return (exitCode, writer.ToString());
	}

	private DataRootEnvironment DataRoot()
	{
		return DataRootEnvironment.Current() with
		{
			GetEnvironmentVariable = name => name == MacroDeckDataRootResolver.DataDirectoryEnvironmentVariable
				? _root
				: null
		};
	}

	private void InstallPlugin(string pluginId, string version, string? manifest)
	{
		WriteVersion(pluginId, version, manifest);
		File.WriteAllText(Path.Combine(PluginsDirectory, pluginId, "current.json"),
			$$"""{ "version": "{{version}}" }""");
	}

	private void WriteVersion(string pluginId, string version, string? manifest)
	{
		var directory = Path.Combine(PluginsDirectory, pluginId, "versions", version);
		Directory.CreateDirectory(directory);
		if (manifest is not null)
		{
			File.WriteAllText(Path.Combine(directory, "manifest.json"), manifest);
			File.WriteAllText(Path.Combine(directory, Executable), string.Empty);
		}
	}

	private static bool CanList(string directory)
	{
		try
		{
			Directory.GetDirectories(directory);
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private Dictionary<string, string> Snapshot()
	{
		return Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
			.ToDictionary(path => path, path => File.Exists(path) ? File.ReadAllText(path) : string.Empty);
	}

	private static string Section(string output, string pluginId)
	{
		var newLine = Environment.NewLine;
		var start = output.IndexOf(pluginId + newLine, StringComparison.Ordinal);
		Assert.That(start, Is.GreaterThanOrEqualTo(0), pluginId + " is not listed");
		var end = output.IndexOf(newLine + newLine, start, StringComparison.Ordinal);
		return end < 0 ? output[start..] : output[start..end];
	}

	private static string Manifest(string pluginId, string name, string version)
	{
		return $$"""
				 {
				 	"manifestVersion": 1,
				 	"id": "{{pluginId}}",
				 	"name": "{{name}}",
				 	"version": "{{version}}",
				 	"entrypoints": {
				 		"win-x64": { "executable": "{{Executable}}" },
				 		"win-arm64": { "executable": "{{Executable}}" },
				 		"linux-x64": { "executable": "{{Executable}}" },
				 		"linux-arm64": { "executable": "{{Executable}}" },
				 		"osx-x64": { "executable": "{{Executable}}" },
				 		"osx-arm64": { "executable": "{{Executable}}" }
				 	}
				 }
				 """;
	}
}
