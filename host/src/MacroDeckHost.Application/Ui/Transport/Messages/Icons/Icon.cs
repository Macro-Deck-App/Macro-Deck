namespace MacroDeckHost.Application.Ui.Transport.Messages.Icons;

public class Icon
{
	public string Id { get; set; } = string.Empty;
	public string PackId { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public int? Width { get; set; }
	public int? Height { get; set; }
	public bool IsAnimated { get; set; }
	public string ProcessingState { get; set; } = string.Empty;
	public string? ProcessingError { get; set; }
	public List<int> AvailableSizes { get; set; } = [];
	public string? OriginalFileName { get; set; }
	public DateTime CreatedAt { get; set; }
}
