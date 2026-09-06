namespace MacroDeckHost.Integrations.SinusBot;

internal sealed class BotIdResponse
{
	public string? DefaultBotId { get; set; }
}

internal sealed class LoginRequest
{
	public string? Username { get; set; }

	public string? Password { get; set; }

	public string? BotId { get; set; }
}

internal class BaseSuccessResponse
{
	public bool? Success { get; set; }
}

internal sealed class LoginResponse : BaseSuccessResponse
{
	public string? Token { get; set; }

	public string? BotId { get; set; }
}

internal sealed class SinusBotInstance
{
	public string? Uuid { get; set; }

	public string? Name { get; set; }

	public string? Nick { get; set; }

	public string? Backend { get; set; }

	public bool? Running { get; set; }

	public bool? Playing { get; set; }

	public string? ServerHost { get; set; }

	public int? ServerPort { get; set; }
}

internal sealed class SinusBotTrack
{
	public string? Title { get; set; }

	public string? Artist { get; set; }

	public long? Duration { get; set; }

	public string? Thumbnail { get; set; }
}

internal sealed class SinusBotFile
{
	public string? Uuid { get; set; }

	public string? Title { get; set; }

	public string? Artist { get; set; }

	public long? Duration { get; set; }

	public string? Thumbnail { get; set; }
}

internal sealed class SinusBotInstanceStatus
{
	public SinusBotTrack? CurrentTrack { get; set; }

	public long? Position { get; set; }

	public bool? Running { get; set; }

	public bool? Playing { get; set; }

	public bool? Shuffle { get; set; }

	public bool? Repeat { get; set; }

	public int? Volume { get; set; }
}
