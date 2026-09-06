using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Adb;

public interface IAdbPlatformToolsInstaller
{
	Task<Result<string, AdbFailureCode>> InstallAsync(CancellationToken cancellationToken);
}
