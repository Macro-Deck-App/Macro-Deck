using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.CompanionApp;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.CompanionApp;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Integrations;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.CompanionApp;

public sealed partial class CompanionAppService : IDisposable
{
	public const string PackageName = "app.macrodeck.companion";
	public const string AutoUpdateKey = "companionApp.autoUpdate";
	public const int MinimumSdkLevel = 23;

	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(6);
	internal static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(15);
	internal static readonly TimeSpan DeviceRecheckInterval = TimeSpan.FromMinutes(5);
	internal static readonly TimeSpan PageRecheckInterval = TimeSpan.FromSeconds(30);

	private readonly IAdbManager _adb;
	private readonly IAdbDeviceOperations _operations;
	private readonly ICompanionAppReleaseClient _releases;
	private readonly CompanionDeviceRegistry _companions;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IUiTransport _ui;
	private readonly IMacroDeckPaths _paths;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly SemaphoreSlim _workGate = new(1, 1);
	private readonly CancellationTokenSource _lifetime = new();
	private readonly Channel<bool> _wake =
		Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
	private readonly Dictionary<string, DeviceEntry> _devices = new(StringComparer.Ordinal);
	private readonly HashSet<(string Serial, int VersionCode)> _autoUpdateAttempts = [];
	private readonly Dictionary<Guid, string> _names = [];

	private CompanionAppRelease? _release;
	private DateTimeOffset? _lastCheckedAt;
	private DateTimeOffset? _lastAttemptAt;
	private bool _checking;
	private bool _checkFailed;
	private bool _refreshRequested;
	private bool? _autoUpdate;
	private string? _lastPushed;
	private DateTimeOffset? _lastDeviceSweepAt;
	private DateTimeOffset? _lastPageSweepAt;

	public CompanionAppService(IAdbManager adb,
		IAdbDeviceOperations operations,
		ICompanionAppReleaseClient releases,
		CompanionDeviceRegistry companions,
		IServiceScopeFactory scopeFactory,
		IUiTransport ui,
		IMacroDeckPaths paths,
		TimeProvider time,
		ILogger logger)
	{
		_adb = adb;
		_operations = operations;
		_releases = releases;
		_companions = companions;
		_scopeFactory = scopeFactory;
		_ui = ui;
		_paths = paths;
		_time = time;
		_logger = logger.ForContext<CompanionAppService>();
		_adb.SnapshotChanged += OnChanged;
		_companions.StateChanged += OnCompanionChanged;
	}

	public async Task<CompanionAppStatus> GetStatusAsync(CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			var now = _time.GetUtcNow();
			if (!_checking && (_lastAttemptAt is null || now - _lastAttemptAt >= StaleAfter) &&
				(_lastCheckedAt is null || now - _lastCheckedAt >= StaleAfter))
			{
				_refreshRequested = true;
				Wake();
			}

			if (_lastPageSweepAt is null || now - _lastPageSweepAt >= PageRecheckInterval)
			{
				_lastPageSweepAt = now;
				MarkAllForProbe(now);
				Wake();
			}
		}

		return await BuildStatusAsync(cancellationToken);
	}

	public async Task<CompanionAppStatus> CheckNowAsync(CancellationToken cancellationToken)
	{
		await _workGate.WaitAsync(cancellationToken);
		try
		{
			SyncDevices();
			await RefreshReleaseAsync(cancellationToken);
			lock (_sync)
			{
				MarkAllForProbe(_time.GetUtcNow());
			}

			await ProbeDueDevicesAsync(cancellationToken);
			await AutoUpdateAsync(cancellationToken);
			return await PushIfChangedAsync(cancellationToken);
		}
		finally
		{
			_workGate.Release();
		}
	}

	public async Task<CompanionAppStatus> SetAutoUpdateAsync(bool enabled, CancellationToken cancellationToken)
	{
		using (var scope = _scopeFactory.CreateScope())
		{
			await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>()
				.SetValue(AutoUpdateKey, enabled ? "true" : "false");
		}

		lock (_sync)
		{
			_autoUpdate = enabled;
		}

		Wake();
		return await PushIfChangedAsync(cancellationToken);
	}

	public async Task<(string? Error, CompanionAppStatus Status)> InstallAsync(string serial,
		CancellationToken cancellationToken)
	{
		await _workGate.WaitAsync(cancellationToken);
		try
		{
			SyncDevices();
			// A closed settings page must not abort an adb install halfway.
			var error = await InstallCoreAsync(serial, automatic: false, _lifetime.Token);
			return (error, await PushIfChangedAsync(_lifetime.Token));
		}
		finally
		{
			_workGate.Release();
		}
	}

	public async Task<(string? Path, CompanionAppRelease? Release, string? Error)> PrepareApkAsync(
		CancellationToken cancellationToken)
	{
		await _workGate.WaitAsync(cancellationToken);
		try
		{
			if (_release is null)
			{
				await RefreshReleaseAsync(cancellationToken);
			}

			if (_release is not { } release)
			{
				return (null, null, CompanionAppErrors.NoRelease);
			}

			var (path, error) = await EnsureApkAsync(release, cancellationToken);
			return (path, release, error);
		}
		finally
		{
			_workGate.Release();
		}
	}

	public async Task<DateTimeOffset?> RunDueWorkAsync(CancellationToken cancellationToken)
	{
		await _workGate.WaitAsync(cancellationToken);
		try
		{
			SyncDevices();
			var interested = Interested();
			if (ReleaseRefreshDue(interested))
			{
				await RefreshReleaseAsync(cancellationToken);
			}

			lock (_sync)
			{
				var now = _time.GetUtcNow();
				if (_devices.Count > 0 && (_lastDeviceSweepAt is null || now - _lastDeviceSweepAt >= DeviceRecheckInterval))
				{
					MarkAllForProbe(now);
				}
			}

			await ProbeDueDevicesAsync(cancellationToken);
			await AutoUpdateAsync(cancellationToken);
			await PushIfChangedAsync(cancellationToken);
			return NextWakeAt(interested);
		}
		finally
		{
			_workGate.Release();
		}
	}

	public async Task WaitForWorkAsync(DateTimeOffset? next, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var read = _wake.Reader.WaitToReadAsync(linked.Token).AsTask();
		var delay = next is { } due
			? Task.Delay(Max(due - _time.GetUtcNow(), TimeSpan.Zero), _time, linked.Token)
			: Task.Delay(Timeout.Infinite, linked.Token);
		await Task.WhenAny(read, delay);
		await linked.CancelAsync();
		_wake.Reader.TryRead(out _);
		cancellationToken.ThrowIfCancellationRequested();
	}

	public void Dispose()
	{
		_adb.SnapshotChanged -= OnChanged;
		_companions.StateChanged -= OnCompanionChanged;
		_lifetime.Cancel();
	}

	private void OnChanged(object? sender, EventArgs e) => Wake();

	private void OnCompanionChanged(object? sender, Guid deviceId) => Wake();

	private void Wake() => _wake.Writer.TryWrite(true);

	private bool Interested() => _adb.Status.Enabled || ConnectedAndroidApps().Count > 0;

	private bool ReleaseRefreshDue(bool interested)
	{
		lock (_sync)
		{
			return _refreshRequested || (interested && ReleaseDueAt() <= _time.GetUtcNow());
		}
	}

	private DateTimeOffset ReleaseDueAt()
	{
		if (_lastAttemptAt is not { } attempted)
		{
			return DateTimeOffset.MinValue;
		}

		return _release is null || _checkFailed || _lastCheckedAt is null
			? attempted + StaleAfter
			: _lastCheckedAt.Value + RefreshInterval;
	}

	// Always in the future: a due time that nothing will act on must not turn the loop into a spin.
	private DateTimeOffset? NextWakeAt(bool interested)
	{
		lock (_sync)
		{
			var now = _time.GetUtcNow();
			DateTimeOffset? next = null;
			if (interested)
			{
				next = ReleaseDueAt();
			}

			if (_devices.Count > 0 && _lastDeviceSweepAt is { } swept)
			{
				var sweep = swept + DeviceRecheckInterval;
				next = next is null || sweep < next ? sweep : next;
			}

			return next is { } due && due <= now ? now + PageRecheckInterval : next;
		}
	}

	private void MarkAllForProbe(DateTimeOffset now)
	{
		_lastDeviceSweepAt = now;
		_names.Clear();
		foreach (var entry in _devices.Values)
		{
			entry.NeedsProbe = entry.AdbState == AdbDeviceState.Device;
		}
	}

	private async Task RefreshReleaseAsync(CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			_checking = true;
		}

		CompanionAppRelease? release;
		try
		{
			await PushIfChangedAsync(cancellationToken);
			release = await _releases.GetLatestAsync(cancellationToken);
		}
		finally
		{
			lock (_sync)
			{
				_checking = false;
				_refreshRequested = false;
			}
		}

		lock (_sync)
		{
			var now = _time.GetUtcNow();
			_lastAttemptAt = now;
			_checkFailed = release is null;
			if (release is not null)
			{
				_release = release;
				_lastCheckedAt = now;
			}
		}
	}

	private void SyncDevices()
	{
		var enabled = _adb.Status.Enabled;
		var devices = enabled ? _adb.Devices : [];
		lock (_sync)
		{
			foreach (var device in devices)
			{
				if (!_devices.TryGetValue(device.Serial, out var entry))
				{
					entry = new DeviceEntry(device.Serial);
					_devices[device.Serial] = entry;
				}

				entry.Name = device.Model ?? device.Serial;
				if (entry.AdbState != device.State)
				{
					entry.AdbState = device.State;
					entry.NeedsProbe = device.IsAuthorized;
				}
			}

			foreach (var serial in _devices.Keys.Where(serial => devices.All(device => device.Serial != serial)).ToList())
			{
				_devices.Remove(serial);
				_autoUpdateAttempts.RemoveWhere(attempt => attempt.Serial == serial);
			}
		}
	}

	private async Task ProbeDueDevicesAsync(CancellationToken cancellationToken)
	{
		List<string> due;
		lock (_sync)
		{
			due = [.. _devices.Values.Where(entry => entry.NeedsProbe && !entry.Installing).Select(entry => entry.Serial)];
		}

		foreach (var serial in due)
		{
			await ProbeAsync(serial, cancellationToken);
		}
	}

	private async Task ProbeAsync(string serial, CancellationToken cancellationToken)
	{
		var sdk = await _adb.QueryAsync(new AdbSdkLevelCommand(serial), cancellationToken);
		Result<string, AdbFailureCode>? package = null;
		int? level = sdk.Success ? CompanionPackageInfoParser.ParseSdkLevel(sdk.Data!) : null;
		if (level >= MinimumSdkLevel)
		{
			package = await _adb.QueryAsync(new AdbPackageInfoCommand(serial, PackageName), cancellationToken);
		}

		lock (_sync)
		{
			if (!_devices.TryGetValue(serial, out var entry))
			{
				return;
			}

			entry.NeedsProbe = false;
			entry.Probed = true;
			entry.SdkLevel = level;
			entry.NotAndroid = level is null && (sdk.Success || sdk.Error == AdbFailureCode.CommandFailed);
			entry.ProbeFailed = (!sdk.Success && !entry.NotAndroid) || package is { Success: false };
			var previous = entry.Package;
			entry.Package = package is { Success: true } found
				? CompanionPackageInfoParser.ParsePackageInfo(found.Data!, PackageName)
				: null;
			if (entry.Package != previous)
			{
				entry.Error = null;
			}
		}
	}

	private async Task AutoUpdateAsync(CancellationToken cancellationToken)
	{
		if (!await AutoUpdateEnabledAsync())
		{
			return;
		}

		List<string> outdated;
		lock (_sync)
		{
			if (_release is not { } release)
			{
				return;
			}

			outdated = [];
			foreach (var entry in _devices.Values)
			{
				if (StateOf(entry, release) == CompanionAppDeviceStates.UpdateAvailable &&
					_autoUpdateAttempts.Add((entry.Serial, release.VersionCode)))
				{
					outdated.Add(entry.Serial);
				}
			}
		}

		foreach (var serial in outdated)
		{
			await InstallCoreAsync(serial, automatic: true, cancellationToken);
		}
	}

	private async Task<string?> InstallCoreAsync(string serial, bool automatic, CancellationToken cancellationToken)
	{
		if (!_adb.Status.Enabled)
		{
			return CompanionAppErrors.AdbDisabled;
		}

		DeviceEntry? entry;
		lock (_sync)
		{
			_devices.TryGetValue(serial, out entry);
		}

		if (entry is null || entry.AdbState != AdbDeviceState.Device)
		{
			return CompanionAppErrors.DeviceNotReady;
		}

		await ProbeAsync(serial, cancellationToken);
		if (entry.ProbeFailed)
		{
			return SetError(entry, CompanionAppErrors.DeviceNotReady);
		}

		if (entry.NotAndroid)
		{
			return CompanionAppErrors.NotAndroid;
		}

		if (entry.SdkLevel < MinimumSdkLevel)
		{
			return CompanionAppErrors.DeviceTooOld;
		}

		if (entry.Package is { FromPlayStore: true })
		{
			return CompanionAppErrors.PlayStoreInstall;
		}

		if (_release is null)
		{
			await RefreshReleaseAsync(cancellationToken);
		}

		if (_release is not { } release)
		{
			return SetError(entry, CompanionAppErrors.NoRelease);
		}

		if (entry.Package?.VersionCode >= release.VersionCode)
		{
			return SetError(entry, null);
		}

		lock (_sync)
		{
			entry.Installing = true;
			entry.Error = null;
		}

		await PushIfChangedAsync(cancellationToken);
		try
		{
			var (apk, downloadError) = await EnsureApkAsync(release, cancellationToken);
			if (apk is null)
			{
				return SetError(entry, downloadError);
			}

			var replacing = entry.Package is not null;
			var relaunch = replacing && (!automatic || await RunningStateAsync(serial, cancellationToken) != "stopped");
			var installed = await _operations.InstallApkAsync(serial, apk, cancellationToken);
			if (!installed.Success)
			{
				_logger.Warning("Installing the Companion app on {Serial} failed: {Output}", serial, installed.ErrorMessage);
				return SetError(entry, InstallFailure(installed));
			}

			_logger.Information("Installed Companion app {Version} on {Serial}", release.Version, serial);
			if (relaunch)
			{
				var started = await _adb.ExecuteAsync(new AdbStartAppCommand(serial, PackageName), cancellationToken);
				if (!started.Success)
				{
					_logger.Warning("Could not start the Companion app on {Serial} after the update: {Error}",
						serial,
						started.ErrorMessage);
				}
			}

			return SetError(entry, null);
		}
		finally
		{
			lock (_sync)
			{
				entry.Installing = false;
			}

			await ProbeAsync(serial, cancellationToken);
		}
	}

	private string? SetError(DeviceEntry entry, string? error)
	{
		lock (_sync)
		{
			entry.Error = error;
		}

		return error;
	}

	private async Task<string> RunningStateAsync(string serial, CancellationToken cancellationToken)
	{
		var result = await _adb.QueryAsync(new AdbPackageRunningCommand(serial, PackageName), cancellationToken);
		return result.Success ? result.Data!.Trim() : "unknown";
	}

	private async Task<(string? Path, string? Error)> EnsureApkAsync(CompanionAppRelease release,
		CancellationToken cancellationToken)
	{
		var directory = Path.Combine(_paths.DataRootDirectory, "companion-app");
		var path = Path.Combine(directory, $"macro-deck-companion-{release.VersionCode.ToString(CultureInfo.InvariantCulture)}.apk");
		try
		{
			Directory.CreateDirectory(directory);
			if (File.Exists(path) && await MatchesAsync(path, release.Sha256, cancellationToken))
			{
				return (path, null);
			}

			var outcome = await _releases.DownloadAsync(release, path, cancellationToken);
			if (outcome != CompanionApkDownloadOutcome.Downloaded)
			{
				return (null, outcome == CompanionApkDownloadOutcome.VerificationFailed
					? CompanionAppErrors.VerificationFailed
					: CompanionAppErrors.DownloadFailed);
			}

			foreach (var stale in Directory.EnumerateFiles(directory, "*.apk").Where(file => file != path))
			{
				File.Delete(stale);
			}

			return (path, null);
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(ex, "Could not prepare the Companion app APK");
			return (null, CompanionAppErrors.DownloadFailed);
		}
	}

	private static async Task<bool> MatchesAsync(string path, string sha256, CancellationToken cancellationToken)
	{
		await using var stream = File.OpenRead(path);
		var hash = await SHA256.HashDataAsync(stream, cancellationToken);
		return string.Equals(Convert.ToHexStringLower(hash), sha256, StringComparison.OrdinalIgnoreCase);
	}

	private static string InstallFailure(Result<AdbFailureCode> result)
	{
		var output = result.ErrorMessage ?? string.Empty;
		if (output.Contains("INSTALL_FAILED_UPDATE_INCOMPATIBLE", StringComparison.Ordinal))
		{
			return CompanionAppErrors.IncompatibleSignature;
		}

		if (output.Contains("INSTALL_FAILED_USER_RESTRICTED", StringComparison.Ordinal))
		{
			return CompanionAppErrors.InstallBlockedOnDevice;
		}

		if (output.Contains("INSTALL_FAILED_INSUFFICIENT_STORAGE", StringComparison.Ordinal))
		{
			return CompanionAppErrors.StorageFull;
		}

		if (output.Contains("INSTALL_FAILED_OLDER_SDK", StringComparison.Ordinal))
		{
			return CompanionAppErrors.DeviceTooOld;
		}

		return result.Error is AdbFailureCode.DeviceUnauthorized or AdbFailureCode.DeviceOffline or AdbFailureCode.DeviceNotFound
			? CompanionAppErrors.DeviceNotReady
			: CompanionAppErrors.InstallFailed;
	}

	private async Task<bool> AutoUpdateEnabledAsync()
	{
		lock (_sync)
		{
			if (_autoUpdate is { } cached)
			{
				return cached;
			}
		}

		using var scope = _scopeFactory.CreateScope();
		var stored = await scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>().GetByKey(AutoUpdateKey);
		var enabled = string.Equals(stored?.Value, "true", StringComparison.Ordinal);
		lock (_sync)
		{
			_autoUpdate ??= enabled;
			return _autoUpdate.Value;
		}
	}

	private List<KeyValuePair<Guid, Integrations.Companion.CompanionDeviceState>> ConnectedAndroidApps()
		=> [.. _companions.ConnectedStates()
			.Where(app => app.Value.Platform?.StartsWith("Android", StringComparison.OrdinalIgnoreCase) == true)];

	private async Task<CompanionAppStatus> PushIfChangedAsync(CancellationToken cancellationToken)
	{
		var status = await BuildStatusAsync(cancellationToken);
		var serialized = JsonSerializer.Serialize(status);
		bool changed;
		lock (_sync)
		{
			changed = serialized != _lastPushed;
			_lastPushed = serialized;
		}

		if (changed)
		{
			try
			{
				await _ui.SendToGroup(UiAdminGroups.Admin, new CompanionAppChangedEvent(), cancellationToken);
			}
			catch (Exception ex) when (ex is not OperationCanceledException)
			{
				_logger.Warning(ex, "Could not announce a Companion app status change");
			}
		}

		return status;
	}

	private async Task<CompanionAppStatus> BuildStatusAsync(CancellationToken cancellationToken)
	{
		var apps = ConnectedAndroidApps();
		await ResolveNamesAsync(apps.Select(app => app.Key), cancellationToken);
		var autoUpdate = await AutoUpdateEnabledAsync();
		lock (_sync)
		{
			var release = _release;
			var latest = release is null ? null : ParseVersion(release.Version);
			return new CompanionAppStatus
			{
				LatestVersion = release?.Version,
				LatestVersionCode = release?.VersionCode,
				PublishedAt = release?.PublishedAt?.ToUnixTimeMilliseconds(),
				LastCheckedAt = _lastCheckedAt?.ToUnixTimeMilliseconds(),
				Checking = _checking || _refreshRequested,
				CheckFailed = _checkFailed,
				AutoUpdate = autoUpdate,
				AdbEnabled = _adb.Status.Enabled,
				Devices =
				[
					.. _devices.Values
						.Where(entry => entry.AdbState is AdbDeviceState.Device or AdbDeviceState.Unauthorized or AdbDeviceState.Authorizing)
						.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
						.Select(entry => new CompanionAppDevice
						{
							Serial = entry.Serial,
							Name = entry.Name,
							State = StateOf(entry, release),
							InstalledVersion = entry.Package?.VersionName,
							InstalledVersionCode = entry.Package?.VersionCode,
							Error = entry.Error
						})
				],
				ConnectedApps =
				[
					.. apps
						.Select(app => new CompanionAppConnectedApp
						{
							DeviceId = app.Key,
							Name = _names.GetValueOrDefault(app.Key) ?? app.Value.Model ?? app.Key.ToString(),
							Model = app.Value.Model,
							AppVersion = app.Value.AppVersion,
							UpdateAvailable = latest is not null &&
								ParseVersion(app.Value.AppVersion) is { } current &&
								current < latest
						})
						.OrderBy(app => app.Name, StringComparer.OrdinalIgnoreCase)
				]
			};
		}
	}

	private async Task ResolveNamesAsync(IEnumerable<Guid> deviceIds, CancellationToken cancellationToken)
	{
		List<Guid> missing;
		lock (_sync)
		{
			missing = [.. deviceIds.Where(id => !_names.ContainsKey(id))];
		}

		if (missing.Count == 0)
		{
			return;
		}

		using var scope = _scopeFactory.CreateScope();
		var repository = scope.ServiceProvider.GetRequiredService<IDeviceRepository>();
		foreach (var id in missing)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if ((await repository.GetById(id))?.Name is { Length: > 0 } name)
			{
				lock (_sync)
				{
					_names[id] = name;
				}
			}
		}
	}

	private static string StateOf(DeviceEntry entry, CompanionAppRelease? release)
	{
		if (entry.AdbState != AdbDeviceState.Device)
		{
			return CompanionAppDeviceStates.NotAuthorized;
		}

		if (entry.Installing)
		{
			return CompanionAppDeviceStates.Installing;
		}

		if (!entry.Probed)
		{
			return CompanionAppDeviceStates.Checking;
		}

		if (entry.NotAndroid)
		{
			return CompanionAppDeviceStates.NotAndroid;
		}

		if (entry.SdkLevel < MinimumSdkLevel)
		{
			return CompanionAppDeviceStates.DeviceTooOld;
		}

		if (entry.ProbeFailed)
		{
			return CompanionAppDeviceStates.Unknown;
		}

		if (entry.Package is not { } package)
		{
			return CompanionAppDeviceStates.NotInstalled;
		}

		if (package.FromPlayStore)
		{
			return CompanionAppDeviceStates.InstalledFromPlayStore;
		}

		if (release is null)
		{
			return CompanionAppDeviceStates.Installed;
		}

		return package.VersionCode >= release.VersionCode
			? CompanionAppDeviceStates.UpToDate
			: CompanionAppDeviceStates.UpdateAvailable;
	}

	internal static Version? ParseVersion(string? value)
	{
		if (value is null || LeadingVersionRegex().Match(value) is not { Success: true } match)
		{
			return null;
		}

		var text = match.Value.Contains('.', StringComparison.Ordinal) ? match.Value : match.Value + ".0";
		return Version.TryParse(text, out var version) ? version : null;
	}

	private static TimeSpan Max(TimeSpan left, TimeSpan right) => left > right ? left : right;

	[GeneratedRegex(@"^\d+(\.\d+){0,3}")]
	private static partial Regex LeadingVersionRegex();

	private sealed class DeviceEntry(string serial)
	{
		public string Serial { get; } = serial;
		public string Name { get; set; } = serial;
		public AdbDeviceState? AdbState { get; set; }
		public bool NeedsProbe { get; set; }
		public bool Probed { get; set; }
		public bool NotAndroid { get; set; }
		public bool ProbeFailed { get; set; }
		public int? SdkLevel { get; set; }
		public CompanionPackageInfo? Package { get; set; }
		public bool Installing { get; set; }
		public string? Error { get; set; }
	}
}
