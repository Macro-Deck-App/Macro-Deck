using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Settings;

public class UpdateLoggingSettingsRequest
{
	public LogEntryLevel? MinimumLevel { get; set; }
}
