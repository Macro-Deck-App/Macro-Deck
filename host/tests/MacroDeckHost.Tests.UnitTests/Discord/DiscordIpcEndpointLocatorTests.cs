using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordIpcEndpointLocatorTests
{
	private static readonly string[] _runtimeThenTmpdirThenTmp = ["/run/user/1000", "/var/folders/tmp", "/tmp"];
	private static readonly string[] _tmpOnly = ["/tmp"];

	[Test]
	public void Ten_named_pipes_are_probed_lowest_first()
	{
		var pipes = DiscordIpcEndpointLocator.WindowsPipeNames();

		Assert.Multiple(() =>
		{
			Assert.That(pipes, Has.Count.EqualTo(DiscordIpcEndpointLocator.SocketCount));
			Assert.That(pipes[0], Is.EqualTo("discord-ipc-0"));
			Assert.That(pipes[^1], Is.EqualTo("discord-ipc-9"));
		});
	}

	[Test]
	public void The_temp_directories_are_probed_in_discords_own_order()
	{
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = "/run/user/1000",
			["TMPDIR"] = "/var/folders/tmp",
			["TMP"] = null,
			["TEMP"] = null
		};

		var directories = DiscordIpcEndpointLocator.UnixSocketDirectories();

		Assert.That(directories, Is.EqualTo(_runtimeThenTmpdirThenTmp));
	}

	[Test]
	public void A_directory_named_twice_is_probed_once()
	{
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = "/tmp",
			["TMPDIR"] = "/tmp/",
			["TMP"] = null,
			["TEMP"] = null
		};

		var directories = DiscordIpcEndpointLocator.UnixSocketDirectories();

		Assert.That(directories, Is.EqualTo(_tmpOnly));
	}

	[Test]
	public void An_unset_variable_is_skipped_rather_than_probed_as_an_empty_path()
	{
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = null,
			["TMPDIR"] = null,
			["TMP"] = "  ",
			["TEMP"] = null
		};

		var directories = DiscordIpcEndpointLocator.UnixSocketDirectories();

		Assert.That(directories, Is.EqualTo(_tmpOnly));
	}

	[Test]
	public void Sandboxed_socket_locations_are_probed_too()
	{
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = "/run/user/1000",
			["TMPDIR"] = null,
			["TMP"] = null,
			["TEMP"] = null
		};

		var paths = DiscordIpcEndpointLocator.UnixSocketPaths();

		Assert.Multiple(() =>
		{
			Assert.That(paths, Does.Contain("/run/user/1000/discord-ipc-0"));
			Assert.That(paths, Does.Contain("/run/user/1000/snap.discord/discord-ipc-0"));
			Assert.That(paths, Does.Contain("/run/user/1000/app/com.discordapp.Discord/discord-ipc-0"));
		});
	}

	[Test]
	public void The_plain_location_is_probed_before_the_sandboxed_ones()
	{
		using var scope = new EnvironmentScope
		{
			["XDG_RUNTIME_DIR"] = "/run/user/1000",
			["TMPDIR"] = null,
			["TMP"] = null,
			["TEMP"] = null
		};

		var paths = DiscordIpcEndpointLocator.UnixSocketPaths();

		Assert.That(paths.Take(DiscordIpcEndpointLocator.SocketCount),
			Is.All.StartsWith("/run/user/1000/discord-ipc-"));
	}

	private sealed class EnvironmentScope : IDisposable
	{
		private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

		public string? this[string name]
		{
			set
			{
				_original.TryAdd(name, Environment.GetEnvironmentVariable(name));
				Environment.SetEnvironmentVariable(name, value);
			}
		}

		public void Dispose()
		{
			foreach (var (name, value) in _original)
			{
				Environment.SetEnvironmentVariable(name, value);
			}
		}
	}
}
