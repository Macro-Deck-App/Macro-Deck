using MacroDeck.Sdk.Android;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>One operation recorded by <see cref="FakeAndroidDevice" />.</summary>
/// <param name="Operation">The <see cref="IAndroidDevice" /> method name, for example <c>InstallApkAsync</c>.</param>
/// <param name="Arguments">The arguments in declaration order, without the cancellation token.</param>
public sealed record AndroidDeviceCall(string Operation, IReadOnlyList<string> Arguments);

/// <summary>
/// A device attached to a <see cref="FakeAndroidDeviceManager" />. Records every operation in <see cref="Calls" />.
/// Shell answers come from <see cref="ShellHandler" />, the battery from <see cref="Battery" />, and installed
/// packages from <see cref="InstalledPackages" />, which install and uninstall keep up to date. Set
/// <see cref="Failure" /> to make every operation throw instead.
/// </summary>
public sealed class FakeAndroidDevice : IAndroidDevice
{
	private readonly FakeAndroidDeviceManager _manager;
	private readonly Lock _gate = new();
	private readonly List<AndroidDeviceCall> _calls = [];
	private AndroidDeviceState _state;

	internal FakeAndroidDevice(FakeAndroidDeviceManager manager,
		string serial,
		AndroidDeviceInfo info,
		AndroidDeviceState state)
	{
		_manager = manager;
		Serial = serial;
		Info = info;
		_state = state;
	}

	/// <inheritdoc />
	public string Serial { get; }

	/// <inheritdoc />
	public AndroidDeviceInfo Info { get; }

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

	/// <summary>Answers <see cref="ExecuteShellAsync" />. By default every command exits 0 with no output.</summary>
	public Func<string, AndroidShellResult> ShellHandler { get; set; } = _ => new AndroidShellResult(0, string.Empty, string.Empty, false);

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
		} ?? Failure;

		if (refusal is { } code)
		{
			throw new AndroidDeviceException(code, $"The fake device refused {operation} with {code}.");
		}
	}
}
