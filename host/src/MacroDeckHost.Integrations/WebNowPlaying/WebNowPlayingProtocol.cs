using System.Globalization;
using System.Text;

namespace MacroDeckHost.Integrations.WebNowPlaying;

internal enum WebNowPlayingMessageType
{
	PlayerAdded = 0,
	PlayerUpdated = 1,
	PlayerRemoved = 2,
	EventResult = 3,
	UseDesktopPlayers = 4
}

internal enum WebNowPlayingEvent
{
	SetState = 0,
	SkipPrevious = 1,
	SkipNext = 2,
	SetPosition = 3,
	SetVolume = 4,
	SetRating = 5,
	SetRepeat = 6,
	SetShuffle = 7
}

internal readonly record struct WebNowPlayingMessage(WebNowPlayingMessageType Type, long Id, string Data);

internal sealed record WebNowPlayingPlayer
{
	public required long Id { get; init; }

	public long PortId { get; init; }

	public string Name { get; init; } = string.Empty;

	public string Title { get; init; } = string.Empty;

	public string Artist { get; init; } = string.Empty;

	public string Album { get; init; } = string.Empty;

	public string CoverSource { get; init; } = string.Empty;

	public int State { get; init; } = WebNowPlayingProtocol.StateStopped;

	public long Position { get; init; }

	public long Duration { get; init; }

	public int Volume { get; init; }

	public int Rating { get; init; }

	public int Repeat { get; init; } = WebNowPlayingProtocol.RepeatNone;

	public bool Shuffle { get; init; }

	public int RatingSystem { get; init; }

	public int AvailableRepeat { get; init; }

	public bool CanSetState { get; init; }

	public bool CanSkipPrevious { get; init; }

	public bool CanSkipNext { get; init; }

	public bool CanSetPosition { get; init; }

	public bool CanSetVolume { get; init; }

	public bool CanSetRating { get; init; }

	public bool CanSetRepeat { get; init; }

	public bool CanSetShuffle { get; init; }

	public long CreatedAt { get; init; }

	public long UpdatedAt { get; init; }

	public long ActiveAt { get; init; }
}

internal static class WebNowPlayingProtocol
{
	// The extension's built-in Macro Deck adapter entry flags an adapter older than the latest release of
	// jbcarreon123/WebNowPlaying-Redux-Macro-Deck as outdated, so this must not fall below that release.
	public const string Handshake = "ADAPTER_VERSION 3.2.0;WNPLIB_REVISION 3";

	public const int StatePlaying = 0;
	public const int StatePaused = 1;
	public const int StateStopped = 2;

	public const int RepeatNone = 1;
	public const int RepeatAll = 2;
	public const int RepeatOne = 4;

	private const int FieldCount = 26;
	private const string EmptyMarker = "\u0001";

	public static bool TryParse(string text, out WebNowPlayingMessage message)
	{
		message = default;
		var parts = text.Split(' ', 3);

		if (parts.Length < 2 ||
			!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var type) ||
			type is < 0 or > 4 ||
			!long.TryParse(parts[1], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var id))
		{
			return false;
		}

		message = new WebNowPlayingMessage((WebNowPlayingMessageType)type,
			id,
			parts.Length == 3 ? parts[2] : string.Empty);
		return true;
	}

	// An empty field leaves the value unchanged, which is how the extension sends partial updates; the
	// 0x01 marker is its spelling of an actually empty value.
	public static WebNowPlayingPlayer ApplyFields(WebNowPlayingPlayer player, string data)
	{
		var fields = SplitFields(data);

		for (var index = 0; index < fields.Count && index < FieldCount; index++)
		{
			var field = fields[index];
			if (field.Length == 0)
			{
				continue;
			}

			var text = field == EmptyMarker ? string.Empty : field;
			long? number = text.Length == 0
				? 0
				: long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
					? parsed
					: null;

			if (index > 5 && number is null)
			{
				continue;
			}

			var value = number ?? 0;
			player = index switch
			{
				0 => value != 0 ? player with { PortId = value } : player,
				1 => player with { Name = text },
				2 => player with { Title = text },
				3 => player with { Artist = text },
				4 => player with { Album = text },
				5 => player with { CoverSource = text },
				6 => player with { State = (int)value },
				7 => player with { Position = value },
				8 => player with { Duration = value },
				9 => player with { Volume = (int)value },
				10 => player with { Rating = (int)value },
				11 => player with { Repeat = (int)value },
				12 => player with { Shuffle = value != 0 },
				13 => player with { RatingSystem = (int)value },
				14 => player with { AvailableRepeat = (int)value },
				15 => player with { CanSetState = value != 0 },
				16 => player with { CanSkipPrevious = value != 0 },
				17 => player with { CanSkipNext = value != 0 },
				18 => player with { CanSetPosition = value != 0 },
				19 => player with { CanSetVolume = value != 0 },
				20 => player with { CanSetRating = value != 0 },
				21 => player with { CanSetRepeat = value != 0 },
				22 => player with { CanSetShuffle = value != 0 },
				23 => player with { CreatedAt = value },
				24 => player with { UpdatedAt = value },
				_ => player with { ActiveAt = value }
			};
		}

		return player;
	}

	public static string Command(long portId, int eventId, WebNowPlayingEvent @event, long data)
		=> string.Create(CultureInfo.InvariantCulture, $"{portId} {eventId} {(int)@event} {data}");

	private static List<string> SplitFields(string data)
	{
		var fields = new List<string>(FieldCount + 1);
		var current = new StringBuilder();

		for (var index = 0; index < data.Length; index++)
		{
			var character = data[index];
			if (character == '\\' && index + 1 < data.Length && data[index + 1] == '|')
			{
				current.Append('|');
				index++;
			}
			else if (character == '|')
			{
				fields.Add(current.ToString());
				current.Clear();
			}
			else
			{
				current.Append(character);
			}
		}

		fields.Add(current.ToString());
		return fields;
	}
}
