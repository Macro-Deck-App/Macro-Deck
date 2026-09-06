namespace MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

public class IntegrationVariableCapability
{
	public string Name { get; set; } = string.Empty;

	public string Type { get; set; } = string.Empty;

	public int? DecimalPlaces { get; set; }

	public double? RefreshIntervalSeconds { get; set; }

	public bool IsTemplate { get; set; }

	public CapabilityAvailability Availability { get; set; }

	public string AvailabilityReason { get; set; } = string.Empty;

	public string? Value { get; set; }

	public bool ValueAvailable { get; set; }
}
