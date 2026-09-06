using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class Device
{
	public string Id { get; set; } = string.Empty;
	public string Name { get; set; } = string.Empty;
	public bool NameIsCustom { get; set; }
	public string? ProposedName { get; set; }

	public string ClientType { get; set; } = "unknown";

	public string FormFactor { get; set; } = "unknown";

	public string? Platform { get; set; }
	public string? Browser { get; set; }
	public string? AppVersion { get; set; }

	public bool Online { get; set; }
	public int ConnectionCount { get; set; }
	public bool HasActiveSession { get; set; }

	public string? StartupProfileId { get; set; }

	public string? StartupProfileName { get; set; }

	public DateTime LastSeenAt { get; set; }

	public DateTime CreatedAt { get; set; }

	/// <summary>Id of the integration or plugin providing this device; absent for a client device.</summary>
	public string? ProviderId { get; set; }

	/// <summary>Display name of the providing integration or plugin.</summary>
	public LocalizedText ProviderName { get; set; }

	public string? ProviderDeviceId { get; set; }

	public string? Model { get; set; }

	public string? Manufacturer { get; set; }

	public string? LayoutReference { get; set; }
}
