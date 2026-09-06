using System.Text.Json;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Actions;

/// <summary>Asks what icon a configured icon-provider action instance currently reports, before the button
/// has adopted it (issue #425) - modelled on <see cref="GetActionProviderStatesRequest" />.</summary>
public class GetActionProviderIconRequest
{
	public string IntegrationId { get; set; } = string.Empty;

	public string ActionId { get; set; } = string.Empty;

	public Dictionary<string, JsonElement>? Parameters { get; set; }
}
