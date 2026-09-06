using System.Text.Json.Serialization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CapabilityAvailability
{
	Ready,
	SetupRequired,
	AvailableAfterSetup,
	IntegrationDisabled,
	Unavailable
}
