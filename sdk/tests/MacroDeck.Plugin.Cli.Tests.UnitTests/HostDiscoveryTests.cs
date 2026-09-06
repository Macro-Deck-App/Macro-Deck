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
	public void SetUp() => _directory = Directory.CreateTempSubdirectory("macrodeck-host-discovery-").FullName;

	[TearDown]
	public void TearDown() => Directory.Delete(_directory, recursive: true);

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
