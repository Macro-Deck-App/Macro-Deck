using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Services;

public record AutostartSettings(bool Supported, bool Enabled, bool OpenMinimized);

public interface IAutostartService
{
	AutostartSettings GetSettings();

	Result<AutostartSettings, AutostartError> Update(bool enabled, bool openMinimized);

	void RefreshRegistration();
}
