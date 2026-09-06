using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Tests.UnitTests.Adb;

internal sealed class FakeAdbPlatformToolsInstaller : IAdbPlatformToolsInstaller
{
	public Result<string, AdbFailureCode> InstallResult { get; set; } =
		Result.Ok<string, AdbFailureCode>("/fake/platform-tools/platform-tools/adb");

	public int InstallCallCount { get; private set; }

	public Task<Result<string, AdbFailureCode>> InstallAsync(CancellationToken cancellationToken)
	{
		InstallCallCount++;
		return Task.FromResult(InstallResult);
	}
}
