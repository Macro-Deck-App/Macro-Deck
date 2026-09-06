using System.Globalization;
using MacroDeckHost.Integrations.Voicemeeter;

namespace MacroDeckHost.Tests.UnitTests.Voicemeeter;

[TestFixture]
internal sealed class VoicemeeterLayoutTests
{
	private static readonly string[] _standardSends = ["A1", "B1"];
	private static readonly string[] _bananaSends = ["A1", "A2", "A3", "B1", "B2"];
	private static readonly string[] _potatoSends = ["A1", "A2", "A3", "A4", "A5", "B1", "B2", "B3"];

	[TestCase(VoicemeeterEdition.Standard, 3, 2, 2, 1)]
	[TestCase(VoicemeeterEdition.Banana, 5, 3, 5, 3)]
	[TestCase(VoicemeeterEdition.Potato, 8, 5, 8, 5)]
	public void Each_edition_has_the_channel_count_Voicemeeter_gives_it(
		VoicemeeterEdition edition,
		int strips,
		int physicalStrips,
		int buses,
		int physicalBuses)
	{
		var layout = VoicemeeterLayout.For(edition);

		Assert.Multiple(() =>
		{
			Assert.That(layout.Strips, Is.EqualTo(strips));
			Assert.That(layout.PhysicalStrips, Is.EqualTo(physicalStrips));
			Assert.That(layout.Buses, Is.EqualTo(buses));
			Assert.That(layout.PhysicalBuses, Is.EqualTo(physicalBuses));
			Assert.That(layout.VirtualBuses, Is.EqualTo(buses - physicalBuses));
		});
	}

	[Test]
	public void An_unknown_edition_has_no_channels()
	{
		var layout = VoicemeeterLayout.For(VoicemeeterEdition.None);

		Assert.Multiple(() =>
		{
			Assert.That(layout, Is.EqualTo(VoicemeeterLayout.None));
			Assert.That(layout.BusAssignmentNames(), Is.Empty);
			Assert.That(layout.BusAssignmentName(0), Is.Null);
		});
	}

	[Test]
	public void Bus_sends_are_named_the_way_the_user_sees_them()
	{
		Assert.Multiple(() =>
		{
			Assert.That(VoicemeeterLayout.For(VoicemeeterEdition.Standard).BusAssignmentNames(),
				Is.EqualTo(_standardSends));
			Assert.That(VoicemeeterLayout.For(VoicemeeterEdition.Banana).BusAssignmentNames(),
				Is.EqualTo(_bananaSends));
			Assert.That(VoicemeeterLayout.For(VoicemeeterEdition.Potato).BusAssignmentNames(),
				Is.EqualTo(_potatoSends));
		});
	}

	[Test]
	public void A_bus_index_maps_onto_its_send_name()
	{
		var banana = VoicemeeterLayout.For(VoicemeeterEdition.Banana);

		Assert.Multiple(() =>
		{
			Assert.That(banana.BusAssignmentName(0), Is.EqualTo("A1"));
			Assert.That(banana.BusAssignmentName(3), Is.EqualTo("B1"));
			Assert.That(banana.BusAssignmentName(5), Is.Null);
			Assert.That(banana.BusAssignmentName(-1), Is.Null);
		});
	}

	[Test]
	public void The_widest_layout_covers_every_edition()
	{
		Assert.Multiple(() =>
		{
			foreach (var edition in Enum.GetValues<VoicemeeterEdition>())
			{
				var layout = VoicemeeterLayout.For(edition);
				Assert.That(VoicemeeterLayout.Widest.Strips, Is.GreaterThanOrEqualTo(layout.Strips));
				Assert.That(VoicemeeterLayout.Widest.Buses, Is.GreaterThanOrEqualTo(layout.Buses));
			}
		});
	}

	[Test]
	public void Only_Banana_and_Potato_have_a_recorder()
	{
		Assert.Multiple(() =>
		{
			Assert.That(VoicemeeterLayout.For(VoicemeeterEdition.Standard).HasRecorder, Is.False);
			Assert.That(VoicemeeterLayout.For(VoicemeeterEdition.Banana).HasRecorder, Is.True);
			Assert.That(VoicemeeterLayout.For(VoicemeeterEdition.Potato).HasRecorder, Is.True);
		});
	}

	[TestCase(1, VoicemeeterEdition.Standard)]
	[TestCase(2, VoicemeeterEdition.Banana)]
	[TestCase(3, VoicemeeterEdition.Potato)]
	[TestCase(0, VoicemeeterEdition.None)]
	[TestCase(9, VoicemeeterEdition.None)]
	public void A_raw_type_maps_onto_an_edition(int rawType, VoicemeeterEdition expected)
		=> Assert.That(VoicemeeterEditions.FromRawType(rawType), Is.EqualTo(expected));

	[Test]
	public void Launching_Potato_asks_for_the_build_matching_the_process()
		=> Assert.That(VoicemeeterEditions.ToRunType(VoicemeeterEdition.Potato),
			Is.EqualTo(Environment.Is64BitProcess ? 6 : 3));

	[Test]
	public void Parameter_names_follow_the_remote_APIs_spelling()
	{
		Assert.Multiple(() =>
		{
			Assert.That(VoicemeeterParameters.Strip(0, VoicemeeterParameters.Gain), Is.EqualTo("Strip[0].Gain"));
			Assert.That(VoicemeeterParameters.Bus(2, VoicemeeterParameters.Eq), Is.EqualTo("Bus[2].EQ.on"));
			Assert.That(VoicemeeterParameters.StripBusAssignment(1, "B2"), Is.EqualTo("Strip[1].B2"));
			Assert.That(VoicemeeterParameters.Command("Restart"), Is.EqualTo("Command.Restart"));
			Assert.That(VoicemeeterParameters.Recorder("play"), Is.EqualTo("Recorder.play"));
		});
	}

	[Test]
	public void The_fade_argument_is_culture_invariant()
	{
		var original = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = new CultureInfo("de-DE");
			Assert.That(VoicemeeterParameters.FadeArgument(-10.5, 500), Is.EqualTo("(-10.5, 500)"));
		}
		finally
		{
			CultureInfo.CurrentCulture = original;
		}
	}
}
