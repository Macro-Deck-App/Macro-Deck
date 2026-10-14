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
/// While Macro Deck is locked, the operations that run arbitrary commands or move files and apps
/// (<see cref="ExecuteShellAsync" />, <see cref="PushFileAsync" />, <see cref="PullFileAsync" />,
/// <see cref="InstallApkAsync" />, <see cref="UninstallPackageAsync" /> and
/// <see cref="IAndroidDeviceManager.ConnectAsync" />) are refused with
/// <see cref="AndroidDeviceErrorCode.HostLocked" />. The fixed, typed operations such as
/// <see cref="SendKeyAsync" />, <see cref="TapAsync" /> or <see cref="RebootAsync" /> run while it is locked, so
/// an action bound to a deck button keeps working. Members added in later versions arrive as default interface
/// members: on an implementation or a Macro Deck that predates one, an added operation throws
/// <see cref="AndroidDeviceException" /> with <see cref="AndroidDeviceErrorCode.Unsupported" /> unless its own
/// documentation says otherwise.
/// </remarks>
public interface IAndroidDevice
{
	string Serial { get; }

	AndroidDeviceInfo Info { get; }

	AndroidDeviceState State { get; }

	/// <summary>
	/// True when the device is <see cref="AndroidDeviceState.Unauthorized" /> because the computer lacks the USB
	/// permissions to talk to it, a Linux udev rule for example, rather than because the device has not authorized
	/// this computer. A change raises <see cref="IAndroidDeviceManager.DeviceStateChanged" /> even when
	/// <see cref="State" /> stays the same. Always false on a Macro Deck that does not report it.
	/// </summary>
	bool LacksUsbPermission => false;

	/// <summary>
	/// For an <see cref="AndroidDeviceState.Offline" /> device, whether it is in the bootloader, recovery or
	/// sideload instead of simply not answering. A change raises <see cref="IAndroidDeviceManager.DeviceStateChanged" />
	/// even when <see cref="State" /> stays the same. Always <see cref="AndroidDeviceMode.Normal" /> on a Macro Deck
	/// that does not report it.
	/// </summary>
	AndroidDeviceMode Mode => AndroidDeviceMode.Normal;

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

	/// <summary>Presses <paramref name="key" /> once.</summary>
	Task SendKeyAsync(AndroidKey key, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(SendKeyAsync));
	}

	/// <summary>Taps the screen at <paramref name="x" />, <paramref name="y" /> in screen pixels from the top left.</summary>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.InvalidArgument" /> for a negative
	/// coordinate.</exception>
	Task TapAsync(int x, int y, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(TapAsync));
	}

	/// <summary>Swipes from one point to another in screen pixels over <paramref name="duration" />.</summary>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.InvalidArgument" /> for a negative
	/// coordinate or duration, or a duration Android does not accept.</exception>
	Task SwipeAsync(
		int fromX,
		int fromY,
		int toX,
		int toY,
		TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(SwipeAsync));
	}

	/// <summary>Types <paramref name="text" /> into the focused field, as if typed on the keyboard.</summary>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.InvalidArgument" /> for empty text or
	/// characters Android's text input cannot type.</exception>
	Task InputTextAsync(string text, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(InputTextAsync));
	}

	/// <summary>Starts the app's launcher activity, or brings the app to the front when it is running.</summary>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.InvalidArgument" /> for a malformed
	/// package name.</exception>
	Task StartAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(StartAppAsync));
	}

	/// <summary>Stops the app and everything it runs, like Force stop in the app's settings.</summary>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.InvalidArgument" /> for a malformed
	/// package name.</exception>
	Task ForceStopAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(ForceStopAppAsync));
	}

	/// <summary>Opens <paramref name="uri" /> with the app the device picks for it, a browser for a web link.</summary>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.InvalidArgument" /> for a URI Macro Deck
	/// does not pass on.</exception>
	Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(OpenUriAsync));
	}

	/// <summary>Restarts the device. It disconnects, and reappears once it is back.</summary>
	Task RebootAsync(AndroidRebootMode mode = AndroidRebootMode.Normal, CancellationToken cancellationToken = default)
	{
		return Unsupported(nameof(RebootAsync));
	}

	/// <summary>Captures the screen as it is now.</summary>
	/// <returns>The PNG file's bytes.</returns>
	/// <exception cref="AndroidDeviceException"><see cref="AndroidDeviceErrorCode.CommandFailed" /> also when a plugin
	/// would receive more than 8 MiB.</exception>
	Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromException<byte[]>(UnsupportedException(nameof(CaptureScreenshotAsync)));
	}

	/// <summary>
	/// The screen state and foreground app, at most a few seconds old: Macro Deck reads them in the background
	/// for every device someone asked about recently and shares the reading between callers.
	/// </summary>
	/// <returns>
	/// <c>null</c> while Macro Deck has no reading of this device yet, for example on the first call, which starts
	/// the readings: ask again a few seconds later. Also <c>null</c> on an implementation, or from a Macro Deck,
	/// that predates this member.
	/// </returns>
	Task<AndroidDeviceStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		return Task.FromResult<AndroidDeviceStatus?>(null);
	}

	private static Task Unsupported(string operation)
	{
		return Task.FromException(UnsupportedException(operation));
	}

	private static AndroidDeviceException UnsupportedException(string operation)
	{
		return new AndroidDeviceException(AndroidDeviceErrorCode.Unsupported,
			$"This Macro Deck does not offer {operation}.");
	}
}
