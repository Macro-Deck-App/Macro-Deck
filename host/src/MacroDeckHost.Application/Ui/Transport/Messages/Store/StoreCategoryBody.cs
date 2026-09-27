namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreCategoryBody
{
	public string Id { get; set; } = string.Empty;

	public Dictionary<string, string> Names { get; set; } = [];

	public int Count { get; set; }
}
