namespace MacroDeckHost.Application.Ui.Transport.Messages.Profiles;

public class Profile
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public int Order { get; set; }
	public string LayoutType { get; set; } = "Grid";
	public bool IsVirtual { get; set; }
	public string? SourceIntegrationId { get; set; }
	public ProfileLayout Layout { get; set; } = new();
	public int DefaultRows { get; set; }
	public int DefaultColumns { get; set; }
	public string? DefaultBackgroundColor { get; set; }

	public int? DefaultWidgetSpacing { get; set; }

	public int? DefaultWidgetBorderRadius { get; set; }
}
