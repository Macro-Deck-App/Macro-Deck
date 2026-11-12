using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.WakeOnLan.Configuration;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.WakeOnLan;

[TestFixture]
internal sealed class WakeOnLanConfigFlowTests
{
	private static Task<ConfigFlowResult> Submit(
		Dictionary<string, object?> input,
		IConfigFlowContext? context = null)
	{
		return new WakeOnLanConfigFlow().SubmitAsync("device",
			input,
			context ?? NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);
	}

	private static Dictionary<string, object?> Device(string mac = "aa:bb:cc:dd:ee:01")
	{
		return new Dictionary<string, object?>
		{
			[WakeOnLanConfigKeys.Name] = " Gaming PC ", [WakeOnLanConfigKeys.MacAddress] = mac
		};
	}

	[TestCase("aa:bb:cc:dd:ee:01")]
	[TestCase("AA-BB-CC-DD-EE-01")]
	[TestCase("aabb.ccdd.ee01")]
	[TestCase("AABBCCDDEE01")]
	[TestCase("  aa:bb:cc:dd:ee:01  ")]
	public async Task A_mac_address_in_a_common_format_is_saved_in_one_canonical_form(string mac)
	{
		var result = await Submit(Device(mac));

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Gaming PC"));
			Assert.That(result.Values![WakeOnLanConfigKeys.MacAddress].Value, Is.EqualTo("AA:BB:CC:DD:EE:01"));
		});
	}

	[TestCase("")]
	[TestCase("aa:bb:cc:dd:ee")]
	[TestCase("aa:bb:cc:dd:ee:01:02")]
	[TestCase("aa:bb-cc:dd:ee:01")]
	[TestCase("gg:bb:cc:dd:ee:01")]
	[TestCase("00:00:00:00:00:00")]
	[TestCase("ff:ff:ff:ff:ff:ff")]
	[TestCase("not a mac")]
	public async Task An_invalid_mac_address_is_refused_on_its_field(string mac)
	{
		var result = await Submit(Device(mac));

		Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { WakeOnLanConfigKeys.MacAddress }));
	}

	[Test]
	public async Task Optional_fields_left_empty_use_the_standard_broadcast_port_and_a_single_packet()
	{
		var input = Device();
		input[WakeOnLanConfigKeys.BroadcastAddress] = "";
		input[WakeOnLanConfigKeys.Port] = null;

		var result = await Submit(input);

		Assert.Multiple(() =>
		{
			Assert.That(result.Values![WakeOnLanConfigKeys.BroadcastAddress].Value, Is.EqualTo("255.255.255.255"));
			Assert.That(result.Values[WakeOnLanConfigKeys.Port].Value, Is.EqualTo("9"));
			Assert.That(result.Values[WakeOnLanConfigKeys.PacketCount].Value, Is.EqualTo("1"));
		});
	}

	[Test]
	public async Task A_directed_broadcast_port_and_repeat_settings_are_saved()
	{
		var input = Device();
		input[WakeOnLanConfigKeys.BroadcastAddress] = "192.168.2.255";
		input[WakeOnLanConfigKeys.Port] = 7d;
		input[WakeOnLanConfigKeys.PacketCount] = 3L;
		input[WakeOnLanConfigKeys.PacketDelay] = "250";

		var result = await Submit(input);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.Values![WakeOnLanConfigKeys.BroadcastAddress].Value, Is.EqualTo("192.168.2.255"));
			Assert.That(result.Values[WakeOnLanConfigKeys.Port].Value, Is.EqualTo("7"));
			Assert.That(result.Values[WakeOnLanConfigKeys.PacketCount].Value, Is.EqualTo("3"));
			Assert.That(result.Values[WakeOnLanConfigKeys.PacketDelay].Value, Is.EqualTo("250"));
			Assert.That(result.Values.Values.Any(value => value.IsSecret), Is.False);
		});
	}

	[TestCase(WakeOnLanConfigKeys.BroadcastAddress, "fe80::1")]
	[TestCase(WakeOnLanConfigKeys.BroadcastAddress, "0.0.0.0")]
	[TestCase(WakeOnLanConfigKeys.BroadcastAddress, "192.168.1")]
	[TestCase(WakeOnLanConfigKeys.BroadcastAddress, "nas.local")]
	[TestCase(WakeOnLanConfigKeys.BroadcastAddress, "192.168.010.255")]
	[TestCase(WakeOnLanConfigKeys.BroadcastAddress, "192.168.1.256")]
	[TestCase(WakeOnLanConfigKeys.Port, 0d)]
	[TestCase(WakeOnLanConfigKeys.Port, 65536d)]
	[TestCase(WakeOnLanConfigKeys.Port, 9.5d)]
	[TestCase(WakeOnLanConfigKeys.PacketCount, 0d)]
	[TestCase(WakeOnLanConfigKeys.PacketCount, 11d)]
	[TestCase(WakeOnLanConfigKeys.PacketDelay, -1d)]
	[TestCase(WakeOnLanConfigKeys.PacketDelay, 10001d)]
	[TestCase(WakeOnLanConfigKeys.PacketDelay, "soon")]
	public async Task An_out_of_range_setting_is_refused_on_its_field(string field, object value)
	{
		var input = Device();
		input[field] = value;

		var result = await Submit(input);

		Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { field }));
	}

	[Test]
	public async Task A_device_without_a_name_is_refused()
	{
		var input = Device();
		input[WakeOnLanConfigKeys.Name] = "  ";

		var result = await Submit(input);

		Assert.That(result.FieldErrors!.Keys, Is.EquivalentTo(new[] { WakeOnLanConfigKeys.Name }));
	}

	[Test]
	public async Task Editing_a_saved_device_keeps_its_name_and_does_not_ask_for_one()
	{
		var context = new ExistingDeviceContext("Office NAS");
		var start = await new WakeOnLanConfigFlow().StartAsync(context, CancellationToken.None);

		var result = await Submit(new Dictionary<string, object?>
			{
				[WakeOnLanConfigKeys.MacAddress] = "AA:BB:CC:DD:EE:01"
			},
			context);

		Assert.Multiple(() =>
		{
			Assert.That(start.NextStep!.Fields.Select(f => f.Name), Does.Not.Contain(WakeOnLanConfigKeys.Name));
			Assert.That(result.EntryTitle, Is.EqualTo("Office NAS"));
			Assert.That(result.Values![WakeOnLanConfigKeys.MacAddress].Value, Is.EqualTo("AA:BB:CC:DD:EE:01"));
		});
	}

	[Test]
	public async Task A_new_device_form_prefills_the_standard_broadcast_address_and_port()
	{
		var start = await new WakeOnLanConfigFlow().StartAsync(NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);
		var fields = start.NextStep!.Fields.ToDictionary(field => field.Name);

		Assert.Multiple(() =>
		{
			Assert.That(fields[WakeOnLanConfigKeys.BroadcastAddress].DefaultValue, Is.EqualTo("255.255.255.255"));
			Assert.That(fields[WakeOnLanConfigKeys.Port].DefaultValue, Is.EqualTo(9d));
		});
	}
}
