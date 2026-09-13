namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class CompanionWakeOnLanEvent
{
	public string InstanceName { get; set; } = string.Empty;

	public List<string> MacAddresses { get; set; } = [];
}
