using System.Text;
using MacroDeckHost.Application.Network.Discovery;
using MacroDeckHost.Infrastructure.Network.Discovery;

namespace MacroDeckHost.Tests.UnitTests.Network.Discovery;

[TestFixture]
internal sealed class DnsSdTxtRecordTests
{
	[Test]
	public void Entries_are_length_prefixed_key_value_strings()
	{
		var encoded = DnsSdTxtRecord.Encode([new TxtEntry("name", "PC"), new TxtEntry("version", "3.1")]);

		byte[] expected = [7, .. "name=PC"u8, 11, .. "version=3.1"u8];
		Assert.That(encoded, Is.EqualTo(expected));
	}

	[Test]
	public void An_entry_is_capped_at_the_255_bytes_a_length_prefix_can_describe()
	{
		var encoded = DnsSdTxtRecord.Encode([new TxtEntry("name", new string('x', 300))]);

		Assert.Multiple(() =>
		{
			Assert.That(encoded[0], Is.EqualTo(255));
			Assert.That(encoded, Has.Length.EqualTo(256));
			Assert.That(Encoding.ASCII.GetString(encoded, 1, 5), Is.EqualTo("name="));
		});
	}
}
