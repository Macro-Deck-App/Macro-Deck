using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Lifecycle;

public record RestartAvailability(bool Supported, string? Reason);

public interface IApplicationRestartService
{
	RestartAvailability Availability { get; }

	bool RestartRequested { get; }

	Result<RestartError> Request(string reason);
}
