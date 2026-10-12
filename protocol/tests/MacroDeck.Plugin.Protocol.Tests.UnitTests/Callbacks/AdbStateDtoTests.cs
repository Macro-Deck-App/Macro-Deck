using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class AdbStateDtoTests
{
	private const string PushWithStableId = """
		{"access":"available","revision":3,"devices":[
			{"serial":"192.168.1.20:5555","state":"online","model":"Pixel 8","stableId":"3fa9c2d1e0b4a756"}]}
		""";

	[Test]
	public void A_reader_built_before_the_stable_id_existed_reads_a_push_that_carries_one()
	{
		var state = JsonSerializer.Deserialize<AdbStateBeforeStableId>(PushWithStableId, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(state.Access, Is.EqualTo(AdbAccessStates.Available));
			Assert.That(state.Revision, Is.EqualTo(3));
			Assert.That(state.Devices.Single(),
				Is.EqualTo(new AdbDeviceBeforeStableId
				{
					Serial = "192.168.1.20:5555", State = AdbDeviceStates.Online, Model = "Pixel 8"
				}));
		});
	}

	[Test]
	public void The_stable_id_is_read_when_present_and_null_from_an_older_host()
	{
		var current = JsonSerializer.Deserialize<AdbStateDto>(PushWithStableId, PluginProtocolJson.Options)!;
		var older = JsonSerializer.Deserialize<AdbStateDto>(
			"""{"access":"available","revision":1,"devices":[{"serial":"A1","state":"online"}]}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(current.Devices.Single().StableId, Is.EqualTo("3fa9c2d1e0b4a756"));
			Assert.That(older.Devices.Single().StableId, Is.Null);
		});
	}

	private sealed record AdbStateBeforeStableId
	{
		public string Access { get; init; } = AdbAccessStates.NotEnabled;

		public IReadOnlyList<AdbDeviceBeforeStableId> Devices { get; init; } = [];

		public long Revision { get; init; }
	}

	private sealed record AdbDeviceBeforeStableId
	{
		public string Serial { get; init; } = string.Empty;

		public string State { get; init; } = AdbDeviceStates.Offline;

		public string? Model { get; init; }

		public string? Manufacturer { get; init; }

		public string? Product { get; init; }
	}
}
