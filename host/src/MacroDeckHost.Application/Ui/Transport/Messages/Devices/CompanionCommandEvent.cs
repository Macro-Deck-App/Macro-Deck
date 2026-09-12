namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class CompanionCommandEvent
{
	public string Command { get; set; } = string.Empty;

	public int? BrightnessPercent { get; set; }

	public string? Orientation { get; set; }
}
