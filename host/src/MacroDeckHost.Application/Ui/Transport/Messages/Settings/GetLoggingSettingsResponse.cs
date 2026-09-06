using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class GetLoggingSettingsResponse
{
	public LogEntryLevel MinimumLevel { get; set; }

	public LogEntryLevel DefaultMinimumLevel { get; set; }
}
