namespace MacroDeckHost.Application.Ui.Transport.Messages.Devices;

public class ReportCompanionStateRequest
{
	public int? BatteryLevelPercent { get; set; }

	public bool Charging { get; set; }

	public string? Orientation { get; set; }

	public int? ScreenBrightnessPercent { get; set; }

	public string? Model { get; set; }

	public string? Platform { get; set; }

	public string? AppVersion { get; set; }

	public List<string>? Capabilities { get; set; }

	public List<string>? RequestableCapabilities { get; set; }

	public bool? InFocus { get; set; }

	public string? NetworkType { get; set; }

	public bool? NetworkMetered { get; set; }

	public bool? NetworkValidated { get; set; }

	public string? NetworkName { get; set; }

	public int? CpuUsagePercent { get; set; }

	public int? MemoryUsedPercent { get; set; }
}
