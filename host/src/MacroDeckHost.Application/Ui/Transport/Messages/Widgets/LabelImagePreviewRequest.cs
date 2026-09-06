namespace MacroDeckHost.Application.Ui.Transport.Messages.Widgets;

public class LabelImagePreviewRequest
{
	public string? Label { get; set; }
	public string? FontFaceId { get; set; }
	public float? FontSize { get; set; }
	public string? TextAlign { get; set; }
	public string? LabelPosition { get; set; }
	public string? LabelColor { get; set; }
	public int WidthCells { get; set; } = 1;
	public int HeightCells { get; set; } = 1;
	public string? ScopeRefId { get; set; }
}
