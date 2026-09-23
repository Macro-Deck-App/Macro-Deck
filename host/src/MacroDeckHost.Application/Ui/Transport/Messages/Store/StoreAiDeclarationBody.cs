namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreAiDeclarationBody
{
	public bool Interaction { get; set; }

	public bool GeneratedContent { get; set; }

	public bool GeneratedAssets { get; set; }

	public List<string> Services { get; set; } = [];
}
