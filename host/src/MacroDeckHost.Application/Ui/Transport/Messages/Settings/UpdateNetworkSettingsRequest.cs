namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateNetworkSettingsRequest
{
	public int PublicPort { get; set; }

	public bool? TlsEnabled { get; set; }

	public string? TlsMode { get; set; }

	public int? TlsHttpsPort { get; set; }

	public bool? DiscoveryEnabled { get; set; }
}
