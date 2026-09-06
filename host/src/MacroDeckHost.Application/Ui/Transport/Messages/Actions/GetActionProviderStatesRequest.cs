using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

/// <summary>Asks what states a configured state-provider action instance currently declares (issue #612).</summary>
public class GetActionProviderStatesRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string ActionId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement>? Parameters { get; set; }
}
