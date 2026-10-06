namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class IconAppearanceResponse
{
	public bool Success { get; set; }
	public TransportError? Error { get; set; }
	public Icon? Icon { get; set; }
	public string? MergedIconId { get; set; }
}
