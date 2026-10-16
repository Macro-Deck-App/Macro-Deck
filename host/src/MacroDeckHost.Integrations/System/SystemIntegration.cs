using System.Globalization;
using System.Threading.Channels;
using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Integrations.System.Migration;
using MacroDeckHost.Integrations.System.State;
using MacroDeckHost.Integrations.System.Variables;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Focus;
using MacroDeckHost.Platform.Lock;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.System;

[MacroDeckIntegration]
public sealed class SystemIntegration
	: IIntegration,
		IVariableProvider,
		IIntegrationIssueProvider,
		IIntegrationIconProvider,
		IMigrationProvider
{
	public const string IntegrationId = "app.macro-deck.system";

	private const string UnsupportedIssueId = "platform-unsupported";
	private const string VolumeIssueId = "volume-unavailable";
	private const string NotificationsIssueId = "notifications-unavailable";
	private const string GpuMetricsIssueId = "gpu-metrics-unavailable";
	private const string PowerIssueId = "power-control-unavailable";

	private const double BytesPerGigabyte = 1024d * 1024d * 1024d;

	private const string PercentUnit = "%";
	private const string GigabyteUnit = "GB";

	private const string VolumePercentId = "system-volume-percent";
	private const string MutedId = "system-muted";
	private const string InputVolumePercentId = "system-input-volume-percent";
	private const string InputMutedId = "system-input-muted";

	private static readonly TimeSpan _discoveryInterval = TimeSpan.FromSeconds(5);

	private const int MaxIndexedGpus = 8;

	private const double MinVolumePercent = 0d;
	private const double MaxVolumePercent = 100d;

	private static readonly byte[] _icon = LoadIcon();

	private static readonly ILogger _logger = IntegrationLog.For<SystemIntegration>(IntegrationId);

	private static readonly VariableDefinition _focusedAppVariable =
		VariableDefinition.Eager("system_focused_app", VariableType.Text)
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.FocusedApp()
			};

	private static readonly VariableDefinition _focusedAppPathVariable =
		VariableDefinition.Eager("system_focused_app_path", VariableType.Text)
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.FocusedAppPath()
			};

	private static readonly VariableDefinition _focusedAppBundleIdVariable =
		VariableDefinition.Eager("system_focused_app_bundle_id", VariableType.Text)
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.FocusedAppBundleId()
			};

	private readonly IApplicationService _applications;
	private readonly IVolumeService _volume;
	private readonly ISystemMetricsService _metrics;
	private readonly IPowerService _power;
	private readonly ILockStateReader _lock;
	private readonly Func<IFocusedApplicationWatcher> _createFocusWatcher;
	private readonly VariableApiAccessor _variableAccessor = new();
	private readonly NotificationServiceAccessor _notificationServiceAccessor = new();
	private readonly DesktopNotifierAccessor _desktopNotifierAccessor = new();

	private Channel<FocusedAppInfo?>? _focusChannel;
	private Task? _focusWriterLoop;
	private IFocusedApplicationWatcher? _focusWatcher;
	private CancellationTokenSource? _focusCancellation;
	private Task? _focusReaderLoop;
	private VariableHandle? _focusedAppHandle;
	private VariableHandle? _focusedAppPathHandle;
	private VariableHandle? _focusedAppBundleIdHandle;

	private readonly IReadOnlyList<VariableDefinition> _fixedVariables;
	private readonly Lock _refreshLock = new();
	private volatile AudioState _audio;
	private volatile IReadOnlyList<string?> _diskSlots = [];
	private volatile IReadOnlyList<VariableDefinition> _variables;
	private readonly Lock _audioSaveLock = new();
	private Task _audioSaveChain = Task.CompletedTask;
	private IIntegrationStorage _storage = UnavailableIntegrationStorage.Instance;
	private bool _persistAudioDevices;
	private CancellationTokenSource? _discoveryCancellation;
	private Task? _discoveryLoop;

	public SystemIntegration()
		: this(ApplicationServiceFactory.Create(IntegrationLog.For(IntegrationId)),
			VolumeServiceFactory.Create(),
			SystemMetricsServiceFactory.Create(),
			PowerServiceFactory.Create(),
			LockStateReaderFactory.Create(),
			FocusedApplicationWatcherFactory.Create)
	{
	}

	// Test seam: lets unit tests inject fake/null services in place of the platform factories.
	internal SystemIntegration(
		IApplicationService applications,
		IVolumeService volume,
		ISystemMetricsService metrics,
		IPowerService power,
		ILockStateReader? lockStateReader = null,
		Func<IFocusedApplicationWatcher>? focusWatcherFactory = null)
	{
		_applications = applications;
		_volume = volume;
		_metrics = metrics;
		_power = power;
		_lock = lockStateReader ?? new NullLockStateReader();
		_createFocusWatcher = focusWatcherFactory ?? (() => new NullFocusedApplicationWatcher());

		_fixedVariables = BuildVariables(_metrics.GpuCount);
		_audio = new AudioState([], new HashSet<string>(StringComparer.Ordinal), null);
		_variables = _fixedVariables;

		Actions =
		[
			new LaunchApplicationActionDefinition(_applications),
			new OpenWebsiteActionDefinition(_applications),
			new OpenFileActionDefinition(_applications),
			new OpenFolderActionDefinition(_applications),
			new KillApplicationActionDefinition(_applications),
			new IncreaseVolumeActionDefinition(_volume, KnownAudioDeviceName),
			new DecreaseVolumeActionDefinition(_volume, KnownAudioDeviceName),
			new MuteVolumeActionDefinition(_volume, KnownAudioDeviceName),
			new SetVolumeActionDefinition(_volume, KnownAudioDeviceName),
			new SendNotificationActionDefinition(_desktopNotifierAccessor),
			new ShowNotificationActionDefinition(_notificationServiceAccessor),
			new RunCommandActionDefinition(_variableAccessor),
			new LockComputerActionDefinition(_power),
			new SleepActionDefinition(_power),
			new HibernateActionDefinition(_power),
			new RestartActionDefinition(_power),
			new ShutDownActionDefinition(_power)
		];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.System.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsInitialized => true;

	public string IconMimeType => "image/svg+xml";

	public byte[] GetIcon()
	{
		return _icon;
	}

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new WindowsUtilsMacroDeck2Migration()];

	public IReadOnlyList<VariableDefinition> Variables => _variables;

	private static IReadOnlyList<VariableDefinition> BuildVariables(int gpuCount)
	{
		return
		[
			VariableDefinition.Eager("system_volume_percent", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Volume(),
					Unit = PercentUnit,
					SemanticKind = VariableSemanticKinds.Percentage,
					Write = new VariableWriteCapability()
				},
			VariableDefinition.Eager("system_muted", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Muted(),
					Write = new VariableWriteCapability()
				},
			VariableDefinition.Eager("system_input_volume_percent", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.InputVolume(),
					Unit = PercentUnit,
					SemanticKind = VariableSemanticKinds.Percentage,
					Write = new VariableWriteCapability()
				},
			VariableDefinition.Eager("system_input_muted",
					VariableType.Boolean,
					refreshInterval: TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.InputMuted(),
					Write = new VariableWriteCapability()
				},
			VariableDefinition.Eager("system_cpu_usage_percent", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.CpuUsage(),
					Unit = PercentUnit,
					SemanticKind = VariableSemanticKinds.Percentage
				},
			VariableDefinition.Eager("system_ram_usage_percent", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.RamUsage(),
					Unit = PercentUnit,
					SemanticKind = VariableSemanticKinds.Percentage
				},
			// Already scaled to gigabytes, so the kind stays none: the bytes formatter would divide it by 1024
			// a second time and report a gigabyte of RAM as a megabyte.
			VariableDefinition.Eager("system_ram_used_gb", VariableType.Numeric, 1, TimeSpan.FromSeconds(2))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.RamUsedGb(),
					Unit = GigabyteUnit,
					SemanticKind = VariableSemanticKinds.None
				},
			VariableDefinition.Eager("system_ram_total_gb", VariableType.Numeric, 1, TimeSpan.FromSeconds(30))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.RamTotalGb(),
					Unit = GigabyteUnit,
					SemanticKind = VariableSemanticKinds.None
				},
			VariableDefinition.Eager("system_cpu_name", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.CpuName()
				},
			VariableDefinition.Eager("system_pc_name", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.PcName()
				},
			VariableDefinition.Eager("system_os", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.OperatingSystem()
				},
			VariableDefinition.Eager("system_date", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(10))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Date()
				},
			VariableDefinition.Eager("system_time", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(1))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Time()
				},
			VariableDefinition.Eager("system_datetime", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(1))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.DateTime()
				},
			VariableDefinition.Eager("system_timestamp_unix", VariableType.Numeric, 0, TimeSpan.FromSeconds(1))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.UnixTimestamp()
				},
			VariableDefinition.Eager("system_day_of_week", VariableType.Text, refreshInterval: TimeSpan.FromSeconds(10))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.DayOfWeek()
				},
			VariableDefinition.Eager("system_hour", VariableType.Numeric, 0, TimeSpan.FromSeconds(1))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Hour()
				},
			VariableDefinition.Eager("system_minute", VariableType.Numeric, 0, TimeSpan.FromSeconds(1))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Minute()
				},
			VariableDefinition.Eager("system_locked", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(1))
				with
				{
					DisplayName = AppStrings.Integrations.System.Variables.Locked()
				},
			.. Enumerable.Range(0, Math.Min(gpuCount, MaxIndexedGpus)).SelectMany(IndexedGpuVariables)
		];
	}

	private static IEnumerable<VariableDefinition> IndexedGpuVariables(int index)
	{
		var label = index.ToString(CultureInfo.InvariantCulture);
		yield return VariableDefinition.Eager($"system_gpu_{index}_usage_percent",
				VariableType.Numeric,
				0,
				TimeSpan.FromSeconds(3))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.GpuUsageIndexed(label),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage
			};
		yield return VariableDefinition.Eager($"system_gpu_{index}_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromMinutes(5))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.GpuNameIndexed(label)
			};
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await StopDiscoveryAsync();
		await StopFocusWatcherAsync();

		_variableAccessor.Current = context.Variables;
		_variableAccessor.UserVariables = context.UserVariables;
		_notificationServiceAccessor.Current = context.NotificationService;
		_desktopNotifierAccessor.Current = context.DesktopNotifications;

		_volume.Changed -= OnVolumeChanged;
		_volume.Changed += OnVolumeChanged;

		_focusedAppHandle = null;
		_focusedAppPathHandle = null;
		_focusedAppBundleIdHandle = null;

		var channel = Channel.CreateBounded<FocusedAppInfo?>(new BoundedChannelOptions(1)
		{
			FullMode = BoundedChannelFullMode.DropOldest,
			SingleReader = true
		});
		_focusChannel = channel;

		_focusWriterLoop = Task.Run(() => RunFocusWriterLoopAsync(channel.Reader));
		// Creates the variables from startup even where no focus watcher is supported.
		channel.Writer.TryWrite(null);

		var watcher = _createFocusWatcher();
		var focusCancellation = new CancellationTokenSource();
		_focusWatcher = watcher;
		_focusCancellation = focusCancellation;
		_focusReaderLoop = Task.Run(() => RunFocusReaderLoopAsync(watcher, channel.Writer, focusCancellation.Token));

		await LoadKnownAudioDevicesAsync(context.Storage);
		// IsInitialized is always true, so the poller may already have registered the fixed list; the
		// first refresh always republishes so the stored devices are declared too.
		await RefreshAudioDevicesAsync(true, CancellationToken.None);
		await RefreshDisksAsync(CancellationToken.None);

		var cancellation = new CancellationTokenSource();
		_discoveryCancellation = cancellation;
		_discoveryLoop = Task.Run(() => RunDiscoveryLoopAsync(cancellation.Token));
	}

	public async Task ShutdownAsync()
	{
		await StopDiscoveryAsync();
		_volume.Changed -= OnVolumeChanged;
		await StopFocusWatcherAsync();
	}

	private async Task StopFocusWatcherAsync()
	{
		if (_focusCancellation is { } cancellation)
		{
			await cancellation.CancelAsync();
		}

		if (_focusReaderLoop is { } readerLoop)
		{
			await readerLoop;
		}

		_focusWatcher?.Dispose();
		_focusCancellation?.Dispose();
		_focusChannel?.Writer.TryComplete();

		if (_focusWriterLoop is { } writerLoop)
		{
			await writerLoop;
		}

		_focusWatcher = null;
		_focusCancellation = null;
		_focusReaderLoop = null;
		_focusChannel = null;
		_focusWriterLoop = null;
	}

	private static async Task RunFocusReaderLoopAsync(
		IFocusedApplicationWatcher watcher,
		ChannelWriter<FocusedAppInfo?> writer,
		CancellationToken cancellationToken)
	{
		if (!watcher.IsSupported)
		{
			return;
		}

		try
		{
			await foreach (var info in watcher.WatchAsync(cancellationToken))
			{
				writer.TryWrite(info);
			}
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "The focused-application watcher stopped");
		}
	}

	internal void UseVariables(IVariableApi variables)
	{
		_variableAccessor.Current = variables;
	}

	internal Task RefreshAudioDevicesAsync()
	{
		return RefreshAudioDevicesAsync(false, CancellationToken.None);
	}

	internal Task RefreshDisksAsync()
	{
		return RefreshDisksAsync(CancellationToken.None);
	}

	private void OnVolumeChanged()
	{
		List<string> ids = [VolumePercentId, MutedId];

		var audio = _audio;
		if (audio.DefaultOutputId is { } defaultOutput
			&& audio.Known.FirstOrDefault(device =>
				device.Flow == AudioDeviceVariables.OutputFlow && device.DeviceId == defaultOutput) is { } known)
		{
			ids.Add(AudioDeviceVariables.VolumeId(known));
			ids.Add(AudioDeviceVariables.MutedId(known));
		}

		RequestDefinitionRefresh(ids);
	}

	private void RequestDefinitionRefresh(List<string> definitionIds)
	{
		if (definitionIds.Count > 0 && _variableAccessor.Current is { } variables)
		{
			_ = variables.RequestDefinitionRefreshAsync(definitionIds);
		}
	}

	private void NotifyDeclarationsChanged()
	{
		_ = _variableAccessor.Current?.NotifyDeclarationsChangedAsync();
	}

	private async Task LoadKnownAudioDevicesAsync(IIntegrationStorage storage)
	{
		_storage = storage;
		IntegrationStorageReadResult read;
		try
		{
			read = await storage.ReadAsync(KnownAudioDevices.StorageKey, KnownAudioDevices.IsValid);
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			_logger.Warning(ex, "Could not read the known audio device list");
			read = IntegrationStorageReadResult.Unreadable;
		}

		// An unreadable list is never written back, or the next save would replace the user's pinned names.
		_persistAudioDevices = read.Status != IntegrationStorageReadStatus.Unreadable;
		if (!_persistAudioDevices)
		{
			_logger.Warning("The known audio device list could not be read; audio devices are not saved this session");
		}

		var stored = read.Content is { } content ? KnownAudioDevices.Deserialize(content) ?? [] : [];
		var audio = _audio;
		_audio = BuildAudioState(AudioDeviceVariables.Valid(stored), audio.Present, audio.DefaultOutputId);
		ComposeVariables();
	}

	private async Task RefreshAudioDevicesAsync(bool republish, CancellationToken cancellationToken)
	{
		IReadOnlyList<AudioDevice> devices;
		try
		{
			devices = _volume.IsSupported ? await _volume.GetDevicesAsync(cancellationToken) : [];
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Could not list the audio devices");
			if (!republish)
			{
				return;
			}

			devices = [];
		}

		bool declarationChanged;
		lock (_refreshLock)
		{
			declarationChanged = ApplyAudioDevices(devices, republish);
		}

		if (declarationChanged && _persistAudioDevices)
		{
			await SaveKnownAudioDevicesAsync();
		}
	}

	// Chained so saves never overlap, and each writes the latest list, whatever order refreshes finish in.
	private Task SaveKnownAudioDevicesAsync()
	{
		lock (_audioSaveLock)
		{
			_audioSaveChain = SaveKnownAudioDevicesAfterAsync(_audioSaveChain);
			return _audioSaveChain;
		}
	}

	private async Task SaveKnownAudioDevicesAfterAsync(Task previous)
	{
		await previous;
		try
		{
			await _storage.WriteAsync(KnownAudioDevices.StorageKey, KnownAudioDevices.Serialize(_audio.Known));
		}
		catch (Exception ex) when (ex is not OutOfMemoryException)
		{
			_logger.Warning(ex, "Could not save the known audio device list");
		}
	}

	private bool ApplyAudioDevices(IReadOnlyList<AudioDevice> devices, bool republish)
	{
		var current = _audio;
		var known = AudioDeviceVariables.Merge(current.Known, devices);
		var declarationChanged = !known.SequenceEqual(current.Known);

		var present = devices.Select(device => PresenceKey(device.Flow, device.Id)).ToHashSet(StringComparer.Ordinal);
		var defaultOutput = devices.FirstOrDefault(device => device is { Flow: AudioFlow.Output, IsDefault: true })?.Id;

		if (!declarationChanged && !republish)
		{
			_audio = current with { Present = present, DefaultOutputId = defaultOutput };
			return false;
		}

		_audio = BuildAudioState(known, present, defaultOutput);
		ComposeVariables();
		NotifyDeclarationsChanged();
		return declarationChanged;
	}

	private static AudioState BuildAudioState(
		IReadOnlyList<KnownAudioDevice> known,
		HashSet<string> present,
		string? defaultOutput)
	{
		return new AudioState(known, present, defaultOutput);
	}

	private async Task RefreshDisksAsync(CancellationToken cancellationToken)
	{
		IReadOnlyList<DiskSample> disks;
		try
		{
			disks = await _metrics.GetDisksAsync(cancellationToken);
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Warning(ex, "Could not list the disks");
			return;
		}

		// A failed read lists no disks at all; keeping the slots stops one bad read from renumbering them.
		if (disks.Count == 0)
		{
			return;
		}

		IReadOnlyList<string?> current;
		IReadOnlyList<string?> slots;
		lock (_refreshLock)
		{
			current = _diskSlots;
			slots = DiskVariables.AssignSlots(current, disks.Select(disk => disk.MountPoint));
			_diskSlots = slots;
			if (slots.Count != current.Count)
			{
				ComposeVariables();
				NotifyDeclarationsChanged();
			}
		}

		var moved = new List<string>();
		for (var index = 0; index < current.Count; index++)
		{
			if (current[index] != slots[index])
			{
				moved.AddRange(DiskVariables.Declare(index).Select(definition => definition.ResolvedId!));
			}
		}

		RequestDefinitionRefresh(moved);
	}

	// Disks come before audio devices so that a remembered-device list loaded over its cap is what the
	// host trims at the eager variable limit, never a disk.
	private void ComposeVariables()
	{
		_variables =
		[
			.. _fixedVariables,
			.. Enumerable.Range(0, _diskSlots.Count).SelectMany(DiskVariables.Declare),
			.. _audio.Known.SelectMany(AudioDeviceVariables.Declare)
		];
	}

	private async Task RunDiscoveryLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(_discoveryInterval, cancellationToken);
				await RefreshAudioDevicesAsync(false, cancellationToken);
				await RefreshDisksAsync(cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task StopDiscoveryAsync()
	{
		if (_discoveryCancellation is not { } cancellation)
		{
			return;
		}

		await cancellation.CancelAsync();
		if (_discoveryLoop is { } loop)
		{
			await loop;
		}

		cancellation.Dispose();
		_discoveryCancellation = null;
		_discoveryLoop = null;
	}

	private AudioTarget? ResolveKnownTarget(AudioFlow flow, string key)
	{
		var audio = _audio;
		var flowName = AudioDeviceVariables.FlowName(flow);
		var device = audio.Known.FirstOrDefault(known => known.Flow == flowName && known.Key == key);
		return device is not null && audio.Present.Contains(PresenceKey(flow, device.DeviceId))
			? new AudioTarget(flow, device.DeviceId)
			: null;
	}

	private string? KnownAudioDeviceName(AudioTarget target)
	{
		var flowName = AudioDeviceVariables.FlowName(target.Flow);
		return target.DeviceId is null
			? null
			: _audio.Known.FirstOrDefault(known => known.Flow == flowName && known.DeviceId == target.DeviceId)?.Name;
	}

	private static string PresenceKey(AudioFlow flow, string deviceId)
	{
		return $"{AudioDeviceVariables.FlowName(flow)}:{deviceId}";
	}

	private sealed record AudioState(
		IReadOnlyList<KnownAudioDevice> Known,
		HashSet<string> Present,
		string? DefaultOutputId);

	// One consumer on a capacity-1 drop-oldest channel serialises the async variable writes, so rapid
	// alt-tabbing can never race and leave a stale value.
	private async Task RunFocusWriterLoopAsync(ChannelReader<FocusedAppInfo?> reader)
	{
		await foreach (var info in reader.ReadAllAsync())
		{
			await WriteFocusVariablesAsync(info);
		}
	}

	private async Task WriteFocusVariablesAsync(FocusedAppInfo? info)
	{
		var api = _variableAccessor.Current;
		if (api is null)
		{
			return;
		}

		try
		{
			_focusedAppHandle ??= await ResolveHandleAsync(api, _focusedAppVariable);
			_focusedAppPathHandle ??= await ResolveHandleAsync(api, _focusedAppPathVariable);
			_focusedAppBundleIdHandle ??= await ResolveHandleAsync(api, _focusedAppBundleIdVariable);

			await api.SetValueAsync(_focusedAppHandle.Id, info?.ProcessName ?? string.Empty);
			await api.SetValueAsync(_focusedAppPathHandle.Id, info?.ExecutablePath ?? string.Empty);
			await api.SetValueAsync(_focusedAppBundleIdHandle.Id, info?.BundleId ?? string.Empty);
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Could not write the focused-application variables");
		}
	}

	private static async Task<VariableHandle> ResolveHandleAsync(IVariableApi api, VariableDefinition declaration)
	{
		return await api.GetByNameAsync(declaration.Name!) ?? await api.CreateAsync(declaration);
	}

	public async ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return localId switch
		{
			VolumePercentId when _volume.IsSupported => await ReadVolumePercentAsync(AudioTarget.DefaultOutput,
				cancellationToken),
			MutedId when _volume.IsSupported =>
				VariableReading.Of(await _volume.GetMuteAsync(AudioTarget.DefaultOutput, cancellationToken)),
			InputVolumePercentId when _volume.IsSupported => await ReadVolumePercentAsync(AudioTarget.DefaultInput,
				cancellationToken),
			InputMutedId when _volume.IsSupported =>
				VariableReading.Of(await _volume.GetMuteAsync(AudioTarget.DefaultInput, cancellationToken)),
			_ when AudioDeviceVariables.TryParseId(localId, out var flow, out var key, out var isVolume) =>
				await ReadAudioDeviceAsync(flow, key, isVolume, cancellationToken),
			"system-cpu-usage-percent" =>
				VariableReading.Of(RoundPercent(await _metrics.GetCpuUsageAsync(cancellationToken))),
			"system-ram-usage-percent" =>
				VariableReading.Of(RamUsagePercent(await _metrics.GetMemoryAsync(cancellationToken))),
			"system-ram-used-gb" =>
				VariableReading.Of(ToGigabytes((await _metrics.GetMemoryAsync(cancellationToken))?.UsedBytes)),
			"system-ram-total-gb" =>
				VariableReading.Of(ToGigabytes((await _metrics.GetMemoryAsync(cancellationToken))?.TotalBytes)),
			"system-cpu-name" => VariableReading.Of(SystemInfo.CpuName),
			"system-pc-name" => VariableReading.Of(SystemInfo.PcName),
			"system-os" => VariableReading.Of(SystemInfo.OsName),
			"system-date" => VariableReading.Of(Now("yyyy-MM-dd")),
			"system-time" => VariableReading.Of(Now("HH:mm:ss")),
			"system-datetime" => VariableReading.Of(Now("yyyy-MM-ddTHH:mm:ss")),
			"system-timestamp-unix" => VariableReading.Of(new DateTimeOffset(DateTime.Now).ToUnixTimeSeconds()),
			"system-day-of-week" => VariableReading.Of(DateTime.Now.DayOfWeek.ToString()),
			"system-hour" => VariableReading.Of(DateTime.Now.Hour),
			"system-minute" => VariableReading.Of(DateTime.Now.Minute),
			"system-locked" when _lock.IsSupported => VariableReading.Of(_lock.IsLocked()),
			_ when DiskVariables.TryParseId(localId, out var diskIndex, out var statistic) =>
				await ReadDiskAsync(diskIndex, statistic, cancellationToken),
			_ => await ReadIndexedGpuAsync(localId, cancellationToken)
		};
	}

	private async ValueTask<VariableReading> ReadDiskAsync(
		int index,
		string statistic,
		CancellationToken cancellationToken)
	{
		var slots = _diskSlots;
		if (index >= slots.Count || slots[index] is not { } mountPoint)
		{
			return VariableReading.Unavailable;
		}

		var disks = await _metrics.GetDisksAsync(cancellationToken);
		return disks.FirstOrDefault(disk => disk.MountPoint == mountPoint) is { } sample
			? VariableReading.Of(DiskVariables.Read(sample, statistic))
			: VariableReading.Unavailable;
	}

	private async ValueTask<VariableReading> ReadVolumePercentAsync(
		AudioTarget target,
		CancellationToken cancellationToken)
	{
		return await _volume.GetVolumeAsync(target, cancellationToken) is { } volume
			? VariableReading.Of((int)Math.Round(volume * 100), MinVolumePercent, MaxVolumePercent, 1)
			: VariableReading.Unavailable;
	}

	private async ValueTask<VariableReading> ReadAudioDeviceAsync(
		AudioFlow flow,
		string key,
		bool isVolume,
		CancellationToken cancellationToken)
	{
		if (!_volume.IsSupported || ResolveKnownTarget(flow, key) is not { } target)
		{
			return VariableReading.Unavailable;
		}

		return isVolume
			? await ReadVolumePercentAsync(target, cancellationToken)
			: VariableReading.Of(await _volume.GetMuteAsync(target, cancellationToken));
	}

	private async ValueTask<VariableReading> ReadIndexedGpuAsync(string localId, CancellationToken cancellationToken)
	{
		if (IndexedGpuNumber(localId, "-usage-percent") is { } usageIndex)
		{
			return VariableReading.Of(RoundPercent(await _metrics.GetGpuUsageAsync(usageIndex, cancellationToken)));
		}

		if (IndexedGpuNumber(localId, "-name") is { } nameIndex)
		{
			return VariableReading.Of(await _metrics.GetGpuNameAsync(nameIndex, cancellationToken));
		}

		return VariableReading.Unavailable;
	}

	private static int? IndexedGpuNumber(string localId, string suffix)
	{
		const string prefix = "system-gpu-";
		if (localId.Length <= prefix.Length + suffix.Length
			|| !localId.StartsWith(prefix, StringComparison.Ordinal)
			|| !localId.EndsWith(suffix, StringComparison.Ordinal))
		{
			return null;
		}

		var digits = localId[prefix.Length..^suffix.Length];
		return int.TryParse(digits, CultureInfo.InvariantCulture, out var index) && index >= 0 ? index : null;
	}

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string localId,
		object? value,
		CancellationToken cancellationToken = default)
	{
		AudioTarget? target;
		bool isVolume;
		if (localId is VolumePercentId or MutedId)
		{
			target = AudioTarget.DefaultOutput;
			isVolume = localId == VolumePercentId;
		}
		else if (localId is InputVolumePercentId or InputMutedId)
		{
			target = AudioTarget.DefaultInput;
			isVolume = localId == InputVolumePercentId;
		}
		else if (AudioDeviceVariables.TryParseId(localId, out var flow, out var key, out isVolume))
		{
			target = ResolveKnownTarget(flow, key);
		}
		else
		{
			return VariableWriteResult.NotWritable();
		}

		if (!_volume.IsSupported || target is not { } resolved)
		{
			return VariableWriteResult.Unavailable();
		}

		bool applied;
		if (isVolume)
		{
			if (!TryReadNumber(value, out var percent))
			{
				return VariableWriteResult.InvalidValue();
			}

			applied = await _volume
				.SetVolumeAsync(resolved,
					(float)(Math.Clamp(percent, MinVolumePercent, MaxVolumePercent) / 100),
					cancellationToken)
				.ConfigureAwait(false);
		}
		else
		{
			if (!TryReadBoolean(value, out var mute))
			{
				return VariableWriteResult.InvalidValue();
			}

			applied = await _volume.SetMuteAsync(resolved, mute, cancellationToken).ConfigureAwait(false);
		}

		return applied ? VariableWriteResult.Applied() : VariableWriteResult.Unavailable();
	}

	private static bool TryReadBoolean(object? value, out bool result)
	{
		switch (value)
		{
			case bool flag:
				result = flag;
				return true;
			case string text when bool.TryParse(text.Trim(), out var parsed):
				result = parsed;
				return true;
			default:
				result = false;
				return false;
		}
	}

	private static bool TryReadNumber(object? value, out double number)
	{
		number = value switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => double.NaN
		};

		return double.IsFinite(number);
	}

	public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken = default)
	{
		var issues = new List<IntegrationIssue>();

		if (!_applications.IsSupported)
		{
			issues.Add(new IntegrationIssue
			{
				Id = UnsupportedIssueId,
				Title = AppStrings.Integrations.System.Issues.UnsupportedTitle(),
				Description = AppStrings.Integrations.System.Issues.UnsupportedDescription(),
				Severity = IntegrationIssueSeverity.Error
			});

			return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
		}

		if (!_volume.IsSupported)
		{
			issues.Add(new IntegrationIssue
			{
				Id = VolumeIssueId,
				Title = AppStrings.Integrations.System.Issues.VolumeUnavailableTitle(),
				Description = AppStrings.Integrations.System.Issues.VolumeUnavailableDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		if (_desktopNotifierAccessor.Current is { IsSupported: false })
		{
			issues.Add(new IntegrationIssue
			{
				Id = NotificationsIssueId,
				Title = AppStrings.Integrations.System.Issues.NotificationsUnavailableTitle(),
				Description = AppStrings.Integrations.System.Issues.NotificationsUnavailableDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		if (_metrics is { IsSupported: true, IsGpuSupported: false })
		{
			issues.Add(new IntegrationIssue
			{
				Id = GpuMetricsIssueId,
				Title = AppStrings.Integrations.System.Issues.GpuMetricsUnavailableTitle(),
				Description = AppStrings.Integrations.System.Issues.GpuMetricsUnavailableDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		if (!_power.IsSupported)
		{
			issues.Add(new IntegrationIssue
			{
				Id = PowerIssueId,
				Title = AppStrings.Integrations.System.Issues.PowerUnavailableTitle(),
				Description = AppStrings.Integrations.System.Issues.PowerUnavailableDescription(),
				Severity = IntegrationIssueSeverity.Warning
			});
		}

		return Task.FromResult<IReadOnlyList<IntegrationIssue>>(issues);
	}

	public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken = default)
	{
		return Task.FromResult(IssueResolution.Failed(AppStrings.Integrations.Issues.CannotResolveAutomatically()));
	}

	private static string Now(string format)
	{
		return DateTime.Now.ToString(format, CultureInfo.InvariantCulture);
	}

	private static int? RoundPercent(double? value)
	{
		return value is { } percent ? (int)Math.Round(percent) : null;
	}

	private static int? RamUsagePercent(MemoryInfo? memory)
	{
		return memory is { TotalBytes: > 0 } info ? (int)Math.Round(info.UsedBytes * 100.0 / info.TotalBytes) : null;
	}

	private static double? ToGigabytes(long? bytes)
	{
		return bytes is { } value ? Math.Round(value / BytesPerGigabyte, 1) : null;
	}

	private static byte[] LoadIcon()
	{
		var assembly = typeof(SystemIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.First(n => n.EndsWith("system-icon.svg", StringComparison.Ordinal));
		using var stream = assembly.GetManifestResourceStream(name)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}
}
