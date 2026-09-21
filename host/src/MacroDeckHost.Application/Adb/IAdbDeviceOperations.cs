using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Adb;

public interface IAdbDeviceOperations
{
	Task<Result<AdbShellOutput, AdbFailureCode>> RunShellAsync(string serial,
		string command,
		CancellationToken cancellationToken);

	Task<Result<AdbBatteryReading, AdbFailureCode>> GetBatteryAsync(string serial, CancellationToken cancellationToken);

	Task<Result<AdbFailureCode>> PushFileAsync(string serial,
		string localPath,
		string remotePath,
		CancellationToken cancellationToken);

	Task<Result<AdbFailureCode>> PullFileAsync(string serial,
		string remotePath,
		string localPath,
		CancellationToken cancellationToken);

	Task<Result<AdbFailureCode>> InstallApkAsync(string serial, string apkPath, CancellationToken cancellationToken);

	Task<Result<AdbFailureCode>> UninstallPackageAsync(string serial,
		string packageName,
		CancellationToken cancellationToken);

	Task<Result<bool, AdbFailureCode>> IsPackageInstalledAsync(string serial,
		string packageName,
		CancellationToken cancellationToken);

	Task<Result<string, AdbFailureCode>> ConnectAsync(string address, CancellationToken cancellationToken);
}
