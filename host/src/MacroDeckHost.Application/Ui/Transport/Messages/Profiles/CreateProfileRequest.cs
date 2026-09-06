namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class CreateProfileRequest
{
	public string Name { get; set; } = string.Empty;
	public int? DefaultRows { get; set; }
	public int? DefaultColumns { get; set; }
	public string? DefaultBackgroundColor { get; set; }
	public int? DefaultWidgetSpacing { get; set; }
	public int? DefaultWidgetBorderRadius { get; set; }
}
