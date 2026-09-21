namespace MacroDeck.Sdk.Android;

/// <summary>
/// One Android device, as Macro Deck's adb sees it. The same instance stays valid while the device goes
/// offline, is unauthorized, or reconnects.
/// </summary>
/// <remarks>
/// Every operation throws <see cref="AndroidDeviceException" /> when it cannot be carried out, with an
/// <see cref="AndroidDeviceException.ErrorCode" /> that tells ADB being off, this plugin not being allowed and
/// device or adb failures apart, and <see cref="OperationCanceledException" /> when cancelled. Paths on the
/// computer are absolute paths on the machine Macro Deck runs on, read and written as the Macro Deck user.
/// Operations that change something are refused with <see cref="AndroidDeviceErrorCode.HostLocked" /> while
/// Macro Deck is locked. Members added in later versions arrive as default interface members.
/// </remarks>
public interface IAndroidDevice
{
	string Serial { get; }

	AndroidDeviceInfo Info { get; }

	AndroidDeviceState State { get; }

	/// <summary>
	/// Runs <paramref name="command" /> in the device's shell. A non-zero exit code is returned, not thrown.
	/// Devices before Android 7 always report exit code 0. Very long output is cut, see
	/// <see cref="AndroidShellResult.Truncated" />. The command must not start with <c>-</c> and gets no input.
	/// </summary>
	Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default);

	/// <summary>The battery state, at most a few seconds old: Macro Deck shares one reading between callers.</summary>
	Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default);

	/// <summary>Copies a local file to <paramref name="remotePath" />, an absolute path on the device.</summary>
	Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default);

	/// <summary>Copies <paramref name="remotePath" /> from the device to a local file, whose directory must exist.</summary>
	Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default);

	/// <summary>Installs a local APK, replacing an installed version of the same app.</summary>
	Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default);

	Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default);

	Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default);
}
