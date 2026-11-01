using MacroDeck.Plugin.Cli.Runtime;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="HostDiscovery" />, the contract behind <c>run</c>'s default host: the running host publishes
/// the loopback port it bound to a port file in the temp directory, named per build channel, and removes
/// it when it stops. The file names and the loopback address are taken from the host's own
/// <c>LoopbackPortFileService</c> and ADR 0002, never read back out of the code under test.
/// </summary>
[TestFixture]
public class HostDiscoveryTests
{
	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Directory.CreateTempSubdirectory("macrodeck-host-discovery-").FullName;
	}

	[TearDown]
	public void TearDown()
	{
		Directory.Delete(_directory, true);
	}

	[TestCase(true, "/tmp/", "/cfg", "/home/a", "/cfg/macro-deck")]
	[TestCase(true, "/tmp", "/cfg", "/home/a", "/cfg/macro-deck")]
	[TestCase(true, "/tmp/", null, "/home/a", "/home/a/.config/macro-deck")]
	[TestCase(true, "/tmp/", "relative", "/home/a", "/home/a/.config/macro-deck")]
	[TestCase(true, "/tmp/", null, "relative", null)]
	[TestCase(true, "/tmp/", null, null, null)]
	[TestCase(true, "/tmp/own/", null, "/home/a", null)]
	[TestCase(false, "/tmp/", null, "/home/a", null)]
	public void The_per_user_directory_applies_only_to_the_shared_linux_tmp(
		bool isLinux,
		string temporaryDirectory,
		string? configHome,
		string? home,
		string? expected)
	{
		var directory = HostDiscovery.PerUserDirectory(isLinux,
			temporaryDirectory,
			name => name == "XDG_CONFIG_HOME" ? configHome : home);

		Assert.That(directory, Is.EqualTo(expected));
	}

	[Test]
	public void The_per_user_file_wins_over_the_temp_directory()
	{
		var perUser = Directory.CreateDirectory(Path.Combine(_directory, "per-user")).FullName;
		File.WriteAllText(Path.Combine(perUser, "macro-deck-host.port"), "6000");
		Write("macro-deck-host.port", "5000", DateTime.UtcNow.AddMinutes(5));

		var discovered = HostDiscovery.Discover(perUser, _directory, _ => ListenerState.Ours);

		Assert.That(discovered?.Url, Is.EqualTo("http://127.0.0.1:6000"));
	}

	[TestCase("Ours")]
	[TestCase("Unknown")]
	public void An_older_host_is_still_found_through_the_temp_directory(string state)
	{
		var perUser = Directory.CreateDirectory(Path.Combine(_directory, "per-user")).FullName;
		Write("macro-deck-host.port", "5000", DateTime.UtcNow);

		var discovered = HostDiscovery.Discover(perUser, _directory, _ => Enum.Parse<ListenerState>(state));

		Assert.That(discovered?.Url, Is.EqualTo("http://127.0.0.1:5000"));
	}

	[TestCase("Foreign")]
	[TestCase("Gone")]
	public void A_temp_file_of_another_user_or_a_dead_port_is_no_host(string state)
	{
		var perUser = Directory.CreateDirectory(Path.Combine(_directory, "per-user")).FullName;
		Write("macro-deck-host.port", "5000", DateTime.UtcNow);

		Assert.That(HostDiscovery.Discover(perUser, _directory, _ => Enum.Parse<ListenerState>(state)), Is.Null);
	}

	[Test]
	public void A_stale_per_user_file_gives_way_to_the_host_in_the_temp_directory()
	{
		var perUser = Directory.CreateDirectory(Path.Combine(_directory, "per-user")).FullName;
		File.WriteAllText(Path.Combine(perUser, "macro-deck-host.port"), "6000");
		Write("macro-deck-host.port", "5000", DateTime.UtcNow);

		var discovered = HostDiscovery.Discover(perUser,
			_directory,
			port => port == 6000 ? ListenerState.Gone : ListenerState.Ours);

		Assert.That(discovered?.Url, Is.EqualTo("http://127.0.0.1:5000"));
	}

	[Test]
	public void A_stale_per_user_file_now_holding_a_foreign_listener_is_no_host()
	{
		var perUser = Directory.CreateDirectory(Path.Combine(_directory, "per-user")).FullName;
		File.WriteAllText(Path.Combine(perUser, "macro-deck-host.port"), "6000");

		Assert.That(HostDiscovery.Discover(perUser, _directory, _ => ListenerState.Foreign), Is.Null);
	}

	[Test]
	public void A_kernel_without_ipv6_still_tells_the_owner_of_an_ipv4_listener()
	{
		const string table = """
							   sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
							    0: 0100007F:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1001        0 11 1 0
							 """;
		var files = new Dictionary<string, string>
		{
			["/proc/net/tcp"] = table,
			["/proc/self/status"] = "Uid:\t1000\t1000\t1000\t1000\n"
		};

		var state = LoopbackListener.Inspect(8080,
			path => files.TryGetValue(path, out var content) ? content : throw new FileNotFoundException(path));

		Assert.That(state, Is.EqualTo(ListenerState.Foreign));
	}

	[Test]
	public void The_proc_tables_tell_own_foreign_and_gone_listeners_apart()
	{
		const string table = """
							   sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
							    0: 0100007F:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 11 1 0
							    1: 0100007F:1F91 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1001        0 12 1 0
							 """;
		const string status = "Name:\tcli\nUid:\t5\t1000\t1000\t1000\n";

		Assert.Multiple(() =>
		{
			Assert.That(LoopbackListener.Classify(table, status, 8080), Is.EqualTo(ListenerState.Ours));
			Assert.That(LoopbackListener.Classify(table, status, 8081), Is.EqualTo(ListenerState.Foreign));
			Assert.That(LoopbackListener.Classify(table, status, 9000), Is.EqualTo(ListenerState.Gone));
			Assert.That(LoopbackListener.Classify("", status, 8080), Is.EqualTo(ListenerState.Unknown));
			Assert.That(LoopbackListener.Classify(table, "", 8080), Is.EqualTo(ListenerState.Unknown));
		});
	}

	[Test]
	public void No_port_file_means_no_host()
	{
		Assert.That(HostDiscovery.Discover(_directory), Is.Null);
	}

	[TestCase("macro-deck-host.port")]
	[TestCase("macro-deck-host-development.port")]
	public void A_published_port_becomes_the_hosts_loopback_url(string fileName)
	{
		Write(fileName, "51234", DateTime.UtcNow);

		var discovered = HostDiscovery.Discover(_directory);

		Assert.Multiple(() =>
		{
			Assert.That(discovered?.Url, Is.EqualTo("http://127.0.0.1:51234"));
			Assert.That(discovered?.PortFilePath, Is.EqualTo(Path.Combine(_directory, fileName)));
		});
	}

	[Test]
	public void With_two_hosts_running_the_one_started_last_wins()
	{
		Write("macro-deck-host.port", "8100", DateTime.UtcNow.AddMinutes(-10));
		Write("macro-deck-host-development.port", "5191", DateTime.UtcNow);

		Assert.That(HostDiscovery.Discover(_directory)?.Url, Is.EqualTo("http://127.0.0.1:5191"));

		Write("macro-deck-host.port", "8100", DateTime.UtcNow.AddMinutes(10));

		Assert.That(HostDiscovery.Discover(_directory)?.Url, Is.EqualTo("http://127.0.0.1:8100"));
	}

	[TestCase("")]
	[TestCase("not a port")]
	[TestCase("0")]
	[TestCase("65536")]
	[TestCase("-1")]
	public void A_port_file_that_holds_no_usable_port_is_no_host(string content)
	{
		Write("macro-deck-host.port", content, DateTime.UtcNow);

		Assert.That(HostDiscovery.Discover(_directory), Is.Null);
	}

	[Test]
	public void A_readable_port_file_still_wins_over_an_unusable_newer_one()
	{
		Write("macro-deck-host.port", "8100", DateTime.UtcNow.AddMinutes(-10));
		Write("macro-deck-host-development.port", "corrupt", DateTime.UtcNow);

		Assert.That(HostDiscovery.Discover(_directory)?.Url, Is.EqualTo("http://127.0.0.1:8100"));
	}

	private void Write(string fileName, string content, DateTime writtenAtUtc)
	{
		var path = Path.Combine(_directory, fileName);
		File.WriteAllText(path, content);
		File.SetLastWriteTimeUtc(path, writtenAtUtc);
	}
}
