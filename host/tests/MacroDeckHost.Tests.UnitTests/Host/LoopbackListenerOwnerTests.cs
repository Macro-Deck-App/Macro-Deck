namespace MacroDeckHost.Tests.UnitTests.Host;

public class LoopbackListenerOwnerTests
{
	private const string Table = """
								   sl  local_address rem_address   st tx_queue rx_queue tr tm->when retrnsmt   uid  timeout inode
								    0: 0100007F:1F90 00000000:0000 0A 00000000:00000000 00:00000000 00000000  1000        0 11 1 0
								    1: 0100007F:C350 0100007F:1F91 01 00000000:00000000 00:00000000 00000000  1001        0 12 1 0
								    2: 00000000000000000000000001000000:1F91 00000000000000000000000000000000:0000 0A 00000000:00000000 00:00000000 00000000  1002        0 13 1 0
								 """;

	[TestCase(8080, 1000L)]
	[TestCase(8081, 1002L)]
	public void The_listener_of_a_port_is_found_with_its_owner(int port, long expected)
	{
		Assert.That(LoopbackListenerOwner.FindListenerUid(Table, port), Is.EqualTo(expected));
	}

	[Test]
	public void A_port_nobody_listens_on_has_no_owner()
	{
		Assert.That(LoopbackListenerOwner.FindListenerUid(Table, 50000), Is.Null);
	}

	[Test]
	public void The_effective_uid_is_read_from_the_process_status()
	{
		Assert.That(LoopbackListenerOwner.ParseUid("Name:\thost\nUid:\t1000\t1001\t1000\t1000\n"), Is.EqualTo(1001));
	}

	[TestCase(1000L, 1000L, ListenerOwner.CurrentUser)]
	[TestCase(1000L, 1001L, ListenerOwner.AnotherUser)]
	[TestCase(null, 1001L, ListenerOwner.Unknown)]
	[TestCase(1000L, null, ListenerOwner.Unknown)]
	public void Ownership_is_only_decided_when_both_uids_are_known(long? listener, long? own, ListenerOwner expected)
	{
		Assert.That(LoopbackListenerOwner.Classify(listener, own), Is.EqualTo(expected));
	}

	[Test]
	public void A_kernel_without_ipv6_still_tells_the_owner_of_an_ipv4_listener()
	{
		var files = new Dictionary<string, string>
		{
			["/proc/net/tcp"] = Table,
			["/proc/self/status"] = "Uid:\t1000\t1000\t1000\t1000\n"
		};

		var owner = LoopbackListenerOwner.Of(8080,
			path => files.TryGetValue(path, out var content) ? content : throw new FileNotFoundException(path));

		Assert.That(owner, Is.EqualTo(ListenerOwner.CurrentUser));
	}
}
