namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class GetIntegrationCapabilitiesResponse
{
	public bool Found { get; set; }

	public bool Enabled { get; set; }

	public bool IsInitialized { get; set; }

	public bool SupportsConfigFlow { get; set; }

	public bool RequiresSetup { get; set; }

	public bool VariablesDependOnConfiguration { get; set; }

	public int ConfiguredEntryCount { get; set; }

	public List<IntegrationActionCapability> Actions { get; set; } = new();

	public List<IntegrationVariableCapability> Variables { get; set; } = new();
}
