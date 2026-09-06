namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class UpdateProfileRequest
{
	public string Id { get; set; } = string.Empty;
	public string? Name { get; set; }
	public int? Order { get; set; }
	public int? DefaultRows { get; set; }
	public int? DefaultColumns { get; set; }
	public string? DefaultBackgroundColor { get; set; }

	public int? DefaultWidgetSpacing { get; set; }

	public int? DefaultWidgetBorderRadius { get; set; }
}
