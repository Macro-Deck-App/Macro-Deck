using MacroDeckHost.Integrations.YtmDesktop;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.YtmDesktop;

[TestFixture]
internal sealed class YtmDesktopVariablesTests
{
	private static readonly VariableDefinition[] _expected =
	[
		Eager("ytmdesktop_is_connected", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(5)),
		Eager("ytmdesktop_current_track_name", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_current_artist", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_current_album", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_album_art_url", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_playback_state", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_is_playing", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_current_position", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)) with
		{
			SemanticKind = VariableSemanticKinds.Duration, Write = MusicPlayerVariableWrites.Position
		},
		Eager("ytmdesktop_track_duration", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)) with
		{
			SemanticKind = VariableSemanticKinds.Duration
		},
		Eager("ytmdesktop_progress_percentage", VariableType.Numeric, 0, TimeSpan.FromSeconds(1)),
		Eager("ytmdesktop_volume", VariableType.Numeric, 0, TimeSpan.FromSeconds(2)) with
		{
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			Write = MusicPlayerVariableWrites.Volume
		},
		Eager("ytmdesktop_is_muted", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_like_status", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_is_liked", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_shuffle_enabled", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(5)),
		Eager("ytmdesktop_repeat_mode", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5)),
		Eager("ytmdesktop_is_live", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(5)),
		Eager("ytmdesktop_media_type", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5)),
		Eager("ytmdesktop_ad_playing", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_video_id", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(2)),
		Eager("ytmdesktop_device_name", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(5))
	];

	private static VariableDefinition Eager(
		string name,
		VariableType type,
		int? decimalPlaces = null,
		TimeSpan? refreshInterval = null)
		=> VariableDefinition.Eager(name, type, decimalPlaces, refreshInterval);

	[Test]
	public void The_catalogue_is_exactly_what_the_issue_asked_for()
	{
		Assert.That(YtmDesktopVariables.All.Select(variable => variable with { DisplayName = default }),
			Is.EqualTo(_expected));
	}

	[Test]
	public void Every_name_carries_the_integration_prefix()
	{
		Assert.Multiple(() =>
		{
			foreach (var variable in YtmDesktopVariables.All)
			{
				Assert.That(variable.Name, Does.StartWith(YtmDesktopVariables.Prefix));
			}
		});
	}

	[Test]
	public void No_variable_refreshes_faster_than_the_polling_floor()
	{
		Assert.Multiple(() =>
		{
			foreach (var variable in YtmDesktopVariables.All)
			{
				Assert.That(variable.RefreshInterval,
					Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(1)),
					variable.Name);
			}
		});
	}
}
