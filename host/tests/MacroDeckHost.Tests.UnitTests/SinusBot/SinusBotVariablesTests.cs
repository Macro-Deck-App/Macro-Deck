using MacroDeckHost.Integrations.SinusBot;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.SinusBot;

[TestFixture]
internal sealed class SinusBotVariablesTests
{
	private static readonly VariableDefinition[] _expected =
	[
		Eager("sinusbot_instance-1_current_track_name", VariableType.Text, refresh: 2),
		Eager("sinusbot_instance-1_current_artist", VariableType.Text, refresh: 2),
		Eager("sinusbot_instance-1_current_album", VariableType.Text, refresh: 2),
		Eager("sinusbot_instance-1_playback_state", VariableType.Text, refresh: 2),
		Eager("sinusbot_instance-1_is_playing", VariableType.Boolean, refresh: 2),
		Eager("sinusbot_instance-1_volume", VariableType.Numeric, 0, 2) with
		{
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			Write = MusicPlayerVariableWrites.Volume
		},
		Eager("sinusbot_instance-1_track_duration", VariableType.Numeric, 0, 2) with
		{
			SemanticKind = VariableSemanticKinds.Duration
		},
		Eager("sinusbot_instance-1_current_position", VariableType.Numeric, 0, 1) with
		{
			SemanticKind = VariableSemanticKinds.Duration, Write = MusicPlayerVariableWrites.Position
		},
		Eager("sinusbot_instance-1_progress_percentage", VariableType.Numeric, 0, 1),
		Eager("sinusbot_instance-1_album_art_url", VariableType.Text, refresh: 2),
		Eager("sinusbot_instance-1_device_name", VariableType.Text, refresh: 5),
		Eager("sinusbot_instance-1_device_type", VariableType.Text, refresh: 5),
		Eager("sinusbot_instance-1_shuffle_enabled", VariableType.Boolean, refresh: 5),
		Eager("sinusbot_instance-1_repeat_mode", VariableType.Text, refresh: 5),
		Eager("sinusbot_instance-1_is_connected", VariableType.Boolean, refresh: 5)
	];

	private static VariableDefinition Eager(string name, VariableType type, int? decimalPlaces = null, int refresh = 0)
		=> VariableDefinition.Eager(name, type, decimalPlaces, TimeSpan.FromSeconds(refresh));

	[Test]
	public void Declare_matches_the_original_inline_shape()
		=> Assert.That(SinusBotVariables.Declare("instance-1")
				.Select(variable => variable with { DisplayName = default }),
			Is.EqualTo(_expected));

	[Test]
	public void Declare_namespaces_by_instance_key()
	{
		var declared = SinusBotVariables.Declare("other-instance");

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(15));
			Assert.That(declared.Select(v => v.Name), Is.All.Contains("other-instance"));
		});
	}
}
