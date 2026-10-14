using MacroDeck.Sdk.Android;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>One operation recorded by <see cref="FakeAndroidDevice" />.</summary>
/// <param name="Operation">The <see cref="IAndroidDevice" /> method name, for example <c>InstallApkAsync</c>.</param>
/// <param name="Arguments">The arguments in declaration order, without the cancellation token.</param>
public sealed record AndroidDeviceCall(string Operation, IReadOnlyList<string> Arguments);

/// <summary>
/// A device attached to a <see cref="FakeAndroidDeviceManager" />. Records every operation in <see cref="Calls" />.
/// Shell answers come from <see cref="ShellHandler" />, the battery from <see cref="Battery" />, the screenshot
/// from <see cref="Screenshot" />, the status from <see cref="Status" />, and installed packages from
/// <see cref="InstalledPackages" />, which install and uninstall keep up to date. Set
/// <see cref="Failure" /> to make every operation throw instead.
/// </summary>
public sealed class FakeAndroidDevice : IAndroidDevice
{
	private readonly FakeAndroidDeviceManager _manager;
	private readonly Lock _gate = new();
	private readonly List<AndroidDeviceCall> _calls = [];
	private AndroidDeviceInfo _info;
	private AndroidDeviceState _state;
	private bool _lacksUsbPermission;
	private AndroidDeviceMode _mode;

	internal FakeAndroidDevice(
		FakeAndroidDeviceManager manager,
		string serial,
		AndroidDeviceInfo info,
		AndroidDeviceState state)
	{
		_manager = manager;
		Serial = serial;
		_info = info;
		_state = state;
	}

	/// <inheritdoc />
	public string Serial { get; }

	/// <inheritdoc />
	public AndroidDeviceInfo Info
	{
		get
		{
			lock (_gate)
			{
				return _info;
			}
		}
	}

	/// <summary>
	/// The <see cref="AndroidDeviceInfo.StableId" /> in <see cref="Info" />. Setting it replaces <see cref="Info" />
	/// without raising an event, like a host that reads the id after the device attached.
	/// </summary>
	public string? StableId
	{
		get => Info.StableId;
		set
		{
			lock (_gate)
			{
				_info = _info with { StableId = value };
			}
		}
	}

	/// <inheritdoc />
	public AndroidDeviceState State
	{
		get
		{
			lock (_gate)
			{
				return _state;
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>Changed with <see cref="FakeAndroidDeviceManager.SetLacksUsbPermission" />.</remarks>
	public bool LacksUsbPermission
	{
		get
		{
			lock (_gate)
			{
				return _lacksUsbPermission;
			}
		}
	}

	/// <inheritdoc />
	/// <remarks>Changed with <see cref="FakeAndroidDeviceManager.SetDeviceMode" />.</remarks>
	public AndroidDeviceMode Mode
	{
		get
		{
			lock (_gate)
			{
				return _mode;
			}
		}
	}

	/// <summary>What <see cref="CaptureScreenshotAsync" /> returns. By default an empty array.</summary>
	public byte[] Screenshot { get; set; } = [];

	/// <summary>What <see cref="GetStatusAsync" /> returns. By default <c>null</c>, like a host without a reading yet.</summary>
	public AndroidDeviceStatus? Status { get; set; }

	/// <summary>Answers <see cref="ExecuteShellAsync" />. By default every command exits 0 with no output.</summary>
	public Func<string, AndroidShellResult> ShellHandler { get; set; } = _
		=> new AndroidShellResult(0, string.Empty, string.Empty, false);

	/// <summary>What <see cref="GetBatteryStateAsync" /> returns.</summary>
	public AndroidBatteryState Battery { get; set; } = new()
	{
		Level = 100, IsCharging = false, Status = AndroidBatteryStatus.Full, Health = AndroidBatteryHealth.Good
	};

	/// <summary>Package names <see cref="IsPackageInstalledAsync" /> reports as installed. An install adds the APK's
	/// file name without its extension; an uninstall removes the package.</summary>
	public ISet<string> InstalledPackages { get; } = new HashSet<string>(StringComparer.Ordinal);

	/// <summary>When set, every operation throws an <see cref="AndroidDeviceException" /> with this code.</summary>
	public AndroidDeviceErrorCode? Failure { get; set; }

	/// <summary>Every operation recorded so far, in call order, including refused ones.</summary>
	public IReadOnlyList<AndroidDeviceCall> Calls
	{
		get
		{
			lock (_gate)
			{
				return [.. _calls];
			}
		}
	}

	/// <inheritdoc />
	public Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default)
	{
		Begin(nameof(ExecuteShellAsync), cancellationToken, command);
		return Task.FromResult(ShellHandler(command));
	}

	/// <inheritdoc />
	public Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
	{
		Begin(nameof(GetBatteryStateAsync), cancellationToken);
		return Task.FromResult(Battery);
	}

	/// <inheritdoc />
	public Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
	{
		Begin(nameof(PushFileAsync), cancellationToken, localPath, remotePath);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
	{
		Begin(nameof(PullFileAsync), cancellationToken, remotePath, localPath);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
	{
		Begin(nameof(InstallApkAsync), cancellationToken, apkPath);
		lock (_gate)
		{
			// Either separator: a test may name a Windows path while running on Linux or macOS.
			var fileName = apkPath[(apkPath.LastIndexOfAny(['/', '\\']) + 1)..];
			InstalledPackages.Add(Path.GetFileNameWithoutExtension(fileName));
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
	{
		Begin(nameof(UninstallPackageAsync), cancellationToken, packageName);
		lock (_gate)
		{
			InstalledPackages.Remove(packageName);
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
	{
		Begin(nameof(IsPackageInstalledAsync), cancellationToken, packageName);
		lock (_gate)
		{
			return Task.FromResult(InstalledPackages.Contains(packageName));
		}
	}

	/// <inheritdoc />
	public Task SendKeyAsync(AndroidKey key, CancellationToken cancellationToken = default)
	{
		Begin(nameof(SendKeyAsync), cancellationToken, key.ToString());
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task TapAsync(int x, int y, CancellationToken cancellationToken = default)
	{
		Begin(nameof(TapAsync), cancellationToken, Invariant(x), Invariant(y));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task SwipeAsync(
		int fromX,
		int fromY,
		int toX,
		int toY,
		TimeSpan duration,
		CancellationToken cancellationToken = default)
	{
		Begin(nameof(SwipeAsync),
			cancellationToken,
			Invariant(fromX),
			Invariant(fromY),
			Invariant(toX),
			Invariant(toY),
			duration.ToString("c", System.Globalization.CultureInfo.InvariantCulture));
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task InputTextAsync(string text, CancellationToken cancellationToken = default)
	{
		Begin(nameof(InputTextAsync), cancellationToken, text);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task StartAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		Begin(nameof(StartAppAsync), cancellationToken, packageName);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task ForceStopAppAsync(string packageName, CancellationToken cancellationToken = default)
	{
		Begin(nameof(ForceStopAppAsync), cancellationToken, packageName);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
	{
		Begin(nameof(OpenUriAsync), cancellationToken, uri);
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task RebootAsync(
		AndroidRebootMode mode = AndroidRebootMode.Normal,
		CancellationToken cancellationToken = default)
	{
		Begin(nameof(RebootAsync), cancellationToken, mode.ToString());
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task<byte[]> CaptureScreenshotAsync(CancellationToken cancellationToken = default)
	{
		Begin(nameof(CaptureScreenshotAsync), cancellationToken);
		return Task.FromResult(Screenshot);
	}

	/// <inheritdoc />
	public Task<AndroidDeviceStatus?> GetStatusAsync(CancellationToken cancellationToken = default)
	{
		Begin(nameof(GetStatusAsync), cancellationToken);
		return Task.FromResult(Status);
	}

	internal void Reattach(AndroidDeviceInfo info, AndroidDeviceState state)
	{
		lock (_gate)
		{
			_info = info;
			_state = state;
			_lacksUsbPermission = false;
			_mode = AndroidDeviceMode.Normal;
		}
	}

	internal void SetMode(AndroidDeviceMode mode)
	{
		lock (_gate)
		{
			_mode = mode;
		}
	}

	internal void SetLacksUsbPermission(bool lacksUsbPermission)
	{
		lock (_gate)
		{
			_lacksUsbPermission = lacksUsbPermission;
		}
	}

	private static string Invariant(int value)
	{
		return value.ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	internal void SetState(AndroidDeviceState state)
	{
		lock (_gate)
		{
			_state = state;
		}
	}

	private void Begin(string operation, CancellationToken cancellationToken, params string[] arguments)
	{
		lock (_gate)
		{
			_calls.Add(new AndroidDeviceCall(operation, arguments));
		}

		cancellationToken.ThrowIfCancellationRequested();

		var refusal = _manager.Access switch
			{
				AndroidDeviceAccess.Available => (AndroidDeviceErrorCode?)null,
				AndroidDeviceAccess.AdbNotEnabled => AndroidDeviceErrorCode.AdbNotEnabled,
				AndroidDeviceAccess.AdbNotAllowed => AndroidDeviceErrorCode.AdbNotAllowed,
				_ => AndroidDeviceErrorCode.Unsupported
			}
			?? Failure;

		if (refusal is { } code)
		{
			throw new AndroidDeviceException(code, $"The fake device refused {operation} with {code}.");
		}
	}
}
