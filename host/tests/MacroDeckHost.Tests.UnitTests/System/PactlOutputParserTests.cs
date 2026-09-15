using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

public class PactlOutputParserTests
{
	// Shape of LC_MESSAGES=C pactl list as printed by pactl 17 (PulseAudio 17).
	private const string Listing = """
		Module #0
			Name: module-device-restore
			Argument:
		Sink #52
			State: SUSPENDED
			Name: alsa_output.pci-0000_00_1f.3.analog-stereo
			Description: Built-in Audio Analog Stereo
			Mute: no
			Volume: front-left: 26214 /  40% / -23.88 dB,   front-right: 26214 /  40% / -23.88 dB
			        balance 0.00
			Base Volume: 65536 / 100% / 0.00 dB
		Source #53
			Name: alsa_output.pci-0000_00_1f.3.analog-stereo.monitor
			Description: Monitor of Built-in Audio Analog Stereo
			Mute: no
		Source #54
			State: RUNNING
			Name: alsa_input.usb-Logitech_Webcam-02.mono-fallback
			Description: Écouteurs Logitech
			Mute: yes
			Volume: mono: 45875 /  70% / -9.29 dB
			Properties:
				device.description = "Logitech Webcam"
		Sink Input #7
			Driver: protocol-native.c
			Mute: no
			Volume: front-left: 65536 / 100% / 0.00 dB
			Properties:
				application.name = "Firefox"
		Client #3
			Driver: protocol-native.c
		""";

	[Test]
	public void A_full_listing_yields_sinks_and_sources_with_mute_and_volume()
	{
		var listing = PactlOutputParser.ParseListing(Listing);

		Assert.Multiple(() =>
		{
			Assert.That(listing.Sinks,
				Is.EqualTo(new[]
				{
					new PactlDevice("alsa_output.pci-0000_00_1f.3.analog-stereo", "Built-in Audio Analog Stereo", false, 0.4f)
				}));
			Assert.That(listing.Sources,
				Is.EqualTo(new[]
				{
					new PactlDevice("alsa_input.usb-Logitech_Webcam-02.mono-fallback", "Écouteurs Logitech", true, 0.7f)
				}));
		});
	}

	[Test]
	public void Defaults_are_read_from_info()
	{
		var defaults = PactlOutputParser.ParseDefaults("""
			Server Name: PulseAudio (on PipeWire 1.0.5)
			Default Sample Specification: s16le 2ch 44100Hz
			Default Sink: alsa_output.pci-0000_00_1f.3.analog-stereo
			Default Source: alsa_input.usb-Logitech_Webcam-02.mono-fallback
			""");

		Assert.That(defaults,
			Is.EqualTo(new PactlDefaults("alsa_output.pci-0000_00_1f.3.analog-stereo",
				"alsa_input.usb-Logitech_Webcam-02.mono-fallback")));
	}

	[TestCase("Mute: yes", true)]
	[TestCase("Mute: no", false)]
	[TestCase("Stumm: ja", null)]
	public void Mute_is_read_from_the_english_label(string output, bool? expected)
	{
		Assert.That(PactlOutputParser.ParseMute(output), Is.EqualTo(expected));
	}

	[Test]
	public void The_pactl_environment_forces_english_messages_and_keeps_the_charset_of_lc_all()
	{
		var environment = PactlOutputParser.UnlocalizedEnvironment("de_DE.UTF-8");

		Assert.That(environment,
			Is.EquivalentTo(new Dictionary<string, string?>
			{
				["LC_ALL"] = null,
				["LC_MESSAGES"] = "C",
				["LC_CTYPE"] = "de_DE.UTF-8"
			}));
	}

	[Test]
	public void Without_lc_all_the_pactl_environment_leaves_the_charset_alone()
	{
		var environment = PactlOutputParser.UnlocalizedEnvironment(null);

		Assert.That(environment.ContainsKey("LC_CTYPE"), Is.False);
	}
}
