namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetColorPaletteRequest
{
}

public class GetColorPaletteResponse
{
	public bool Success { get; set; } = true;
	public TransportError? Error { get; set; }

	public IReadOnlyList<string> Colors { get; set; } = [];
}

public class SetColorPaletteEntryRequest
{
	public string Color { get; set; } = string.Empty;

	public bool Present { get; set; }
}

public class SetColorPaletteEntryResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }

	public IReadOnlyList<string> Colors { get; set; } = [];
}

public class RestoreDefaultColorPaletteRequest
{
}

public sealed record ColorPaletteChangedEvent
{
	public IReadOnlyList<string> Colors { get; init; } = [];
}
