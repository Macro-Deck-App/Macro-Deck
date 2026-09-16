using System.Globalization;
using System.Threading.Channels;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Integrations.System.Application;
using MacroDeckHost.Integrations.System.Focus;
using MacroDeckHost.Integrations.System.Lock;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Localization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Migration;
using MacroDeck.Sdk.Variables;
using Serilog;

namespace MacroDeckHost.Integrations.System;

[MacroDeckIntegration]
public sealed class SystemIntegration
	: IIntegration,
		IVariableProvider,
		IIntegrationIssueProvider,
		IIntegrationIconProvider,
		IMigrationProvider,
		IVariableRefreshSignalConsumer,
		IVariablePollingInvalidationConsumer,
		IKnownAudioDeviceStoreConsumer
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

	private static readonly TimeSpan _audioDiscoveryInterval = TimeSpan.FromSeconds(5);

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
	private readonly INotificationService _notifications;
	private readonly ISystemMetricsService _metrics;
	private readonly IPowerService _power;
	private readonly ILockStateReader _lock;
	private readonly VariableApiAccessor _variableAccessor = new();

	private Channel<FocusedAppInfo?>? _focusChannel;
	private Task? _focusWriterLoop;
	private VariableHandle? _focusedAppHandle;
	private VariableHandle? _focusedAppPathHandle;
	private VariableHandle? _focusedAppBundleIdHandle;
	private IVariableRefreshSignal? _refreshSignal;

	private readonly IReadOnlyList<VariableDefinition> _fixedVariables;
	private volatile AudioState _audio;
	private IKnownAudioDeviceStore? _audioStore;
	private IVariablePollingInvalidationSignal? _pollingInvalidation;
	private bool _persistAudioDevices;
	private CancellationTokenSource? _audioDiscoveryCancellation;
	private Task? _audioDiscoveryLoop;

	public SystemIntegration()
		: this(ApplicationServiceFactory.Create(),
			VolumeServiceFactory.Create(),
			NotificationServiceFactory.Create(),
			SystemMetricsServiceFactory.Create(),
			PowerServiceFactory.Create(),
			LockStateReaderFactory.Create())
	{
	}

	// Test seam: lets unit tests inject fake/null services in place of the platform factories.
	internal SystemIntegration(
		IApplicationService applications,
		IVolumeService volume,
		INotificationService notifications,
		ISystemMetricsService metrics,
		IPowerService power,
		ILockStateReader? lockStateReader = null)
	{
		_applications = applications;
		_volume = volume;
		_notifications = notifications;
		_metrics = metrics;
		_power = power;
		_lock = lockStateReader ?? new NullLockStateReader();

		_fixedVariables = BuildVariables(_metrics.GpuCount);
		_audio = new AudioState([], new HashSet<string>(StringComparer.Ordinal), null, _fixedVariables);

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
			new SendNotificationActionDefinition(_notifications),
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

	public byte[] GetIcon() => _icon;

	public IReadOnlyList<IIntegrationMigration> Migrations { get; } = [new WindowsUtilsMacroDeck2Migration()];

	public IReadOnlyList<VariableDefinition> Variables => _audio.Variables;

	private static IReadOnlyList<VariableDefinition> BuildVariables(int gpuCount) =>
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
				DisplayName = AppStrings.Integrations.System.Variables.Muted()
			},
		VariableDefinition.Eager("system_input_volume_percent", VariableType.Numeric, 0, TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.InputVolume(),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage,
				Write = new VariableWriteCapability()
			},
		VariableDefinition.Eager("system_input_muted", VariableType.Boolean, refreshInterval: TimeSpan.FromSeconds(2))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.InputMuted()
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

	private static IEnumerable<VariableDefinition> IndexedGpuVariables(int index)
	{
		var label = index.ToString(CultureInfo.InvariantCulture);
		yield return VariableDefinition.Eager($"system_gpu_{index}_usage_percent",
				VariableType.Numeric,
				0,
				TimeSpan.FromSeconds(3))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.GpuUsageIndexed(index: label),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage
			};
		yield return VariableDefinition.Eager($"system_gpu_{index}_name",
				VariableType.Text,
				refreshInterval: TimeSpan.FromMinutes(5))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.GpuNameIndexed(index: label)
			};
	}

	public async Task InitializeAsync(IIntegrationContext context)
	{
		await StopAudioDiscoveryAsync();

		_variableAccessor.Current = context.Variables;

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

		// Subscribed before the seed write below, so a focus change racing the seed is never lost - at
		// worst the same value is written twice, which a capacity-1 drop-oldest channel already tolerates.
		FocusedApplicationSnapshot.Current.Changed += OnFocusChanged;
		_focusWriterLoop = Task.Run(() => RunFocusWriterLoopAsync(channel.Reader));
		channel.Writer.TryWrite(FocusedApplicationSnapshot.Current.Value);

		LoadKnownAudioDevices();
		// IsInitialized is always true, so the poller may already have registered the fixed list; the
		// first refresh always republishes so the stored devices are declared too.
		await RefreshAudioDevicesAsync(republish: true, CancellationToken.None);

		var cancellation = new CancellationTokenSource();
		_audioDiscoveryCancellation = cancellation;
		_audioDiscoveryLoop = Task.Run(() => RunAudioDiscoveryLoopAsync(cancellation.Token));
	}

	public async Task ShutdownAsync()
	{
		await StopAudioDiscoveryAsync();
		_volume.Changed -= OnVolumeChanged;
		FocusedApplicationSnapshot.Current.Changed -= OnFocusChanged;
		_focusChannel?.Writer.TryComplete();

		if (_focusWriterLoop is { } loop)
		{
			await loop;
		}

		_focusWriterLoop = null;
	}

	private void OnFocusChanged(FocusedAppInfo? info) => _focusChannel?.Writer.TryWrite(info);

	public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => _refreshSignal = signal;

	public void UseVariablePollingInvalidation(IVariablePollingInvalidationSignal signal)
		=> _pollingInvalidation = signal;

	public void UseKnownAudioDeviceStore(IKnownAudioDeviceStore store) => _audioStore = store;

	internal Task RefreshAudioDevicesAsync() => RefreshAudioDevicesAsync(republish: false, CancellationToken.None);

	private void OnVolumeChanged()
	{
		_refreshSignal?.RequestDefinitionRefresh(IntegrationId, VolumePercentId);
		_refreshSignal?.RequestDefinitionRefresh(IntegrationId, MutedId);

		var audio = _audio;
		if (audio.DefaultOutputId is { } defaultOutput &&
			audio.Known.FirstOrDefault(device =>
				device.Flow == AudioDeviceVariables.OutputFlow && device.DeviceId == defaultOutput) is { } known)
		{
			_refreshSignal?.RequestDefinitionRefresh(IntegrationId, AudioDeviceVariables.VolumeId(known));
			_refreshSignal?.RequestDefinitionRefresh(IntegrationId, AudioDeviceVariables.MutedId(known));
		}
	}

	private void LoadKnownAudioDevices()
	{
		IReadOnlyList<KnownAudioDevice> stored = [];
		_persistAudioDevices = _audioStore is not null && _audioStore.TryLoad(out stored);
		if (_audioStore is not null && !_persistAudioDevices)
		{
			_logger.Warning("The known audio device list could not be read; audio devices are not saved this session");
		}

		var audio = _audio;
		_audio = BuildAudioState(AudioDeviceVariables.Valid(stored), audio.Present, audio.DefaultOutputId);
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

		var current = _audio;
		var known = AudioDeviceVariables.Merge(current.Known, devices);
		var declarationChanged = !known.SequenceEqual(current.Known);
		if (declarationChanged && _persistAudioDevices)
		{
			_audioStore!.Save(known);
		}

		var present = devices.Select(device => PresenceKey(device.Flow, device.Id)).ToHashSet(StringComparer.Ordinal);
		var defaultOutput = devices.FirstOrDefault(device => device is { Flow: AudioFlow.Output, IsDefault: true })?.Id;

		if (!declarationChanged && !republish)
		{
			_audio = current with { Present = present, DefaultOutputId = defaultOutput };
			return;
		}

		_audio = BuildAudioState(known, present, defaultOutput);
		_pollingInvalidation?.MarkStale(IntegrationId);
	}

	private AudioState BuildAudioState(
		IReadOnlyList<KnownAudioDevice> known,
		HashSet<string> present,
		string? defaultOutput)
		=> new(known, present, defaultOutput, [.. _fixedVariables, .. known.SelectMany(AudioDeviceVariables.Declare)]);

	private async Task RunAudioDiscoveryLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				await Task.Delay(_audioDiscoveryInterval, cancellationToken);
				await RefreshAudioDevicesAsync(republish: false, cancellationToken);
			}
		}
		catch (OperationCanceledException)
		{
		}
	}

	private async Task StopAudioDiscoveryAsync()
	{
		if (_audioDiscoveryCancellation is not { } cancellation)
		{
			return;
		}

		await cancellation.CancelAsync();
		if (_audioDiscoveryLoop is { } loop)
		{
			await loop;
		}

		cancellation.Dispose();
		_audioDiscoveryCancellation = null;
		_audioDiscoveryLoop = null;
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
		=> $"{AudioDeviceVariables.FlowName(flow)}:{deviceId}";

	private sealed record AudioState(
		IReadOnlyList<KnownAudioDevice> Known,
		HashSet<string> Present,
		string? DefaultOutputId,
		IReadOnlyList<VariableDefinition> Variables);

	// One consumer draining a capacity-1 drop-oldest channel, rather than an unordered Task per focus
	// change: the variable API is async and Changed fires synchronously on the focus pipeline's own
	// thread, so writes must be serialised here or rapid alt-tabbing could race and leave a stale value.
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
		=> await api.GetByNameAsync(declaration.Name!) ?? await api.CreateAsync(declaration);

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
			// The state the host has already established, so this variable and the lock state pushed to
			// clients can never momentarily disagree; the reader answers only until the first one lands.
			"system-locked" when _lock.IsSupported =>
				VariableReading.Of(LockStateSnapshot.Current.Value ?? _lock.IsLocked()),
			_ => await ReadIndexedGpuAsync(localId, cancellationToken)
		};
	}

	private async ValueTask<VariableReading> ReadVolumePercentAsync(AudioTarget target, CancellationToken cancellationToken)
		=> await _volume.GetVolumeAsync(target, cancellationToken) is { } volume
			? VariableReading.Of((int)Math.Round(volume * 100), MinVolumePercent, MaxVolumePercent, 1)
			: VariableReading.Unavailable;

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
		if (localId.Length <= prefix.Length + suffix.Length ||
			!localId.StartsWith(prefix, StringComparison.Ordinal) ||
			!localId.EndsWith(suffix, StringComparison.Ordinal))
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
		if (localId == VolumePercentId)
		{
			target = AudioTarget.DefaultOutput;
		}
		else if (localId == InputVolumePercentId)
		{
			target = AudioTarget.DefaultInput;
		}
		else if (AudioDeviceVariables.TryParseId(localId, out var flow, out var key, out var isVolume) && isVolume)
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

		if (!TryReadNumber(value, out var percent))
		{
			return VariableWriteResult.InvalidValue();
		}

		var applied = await _volume
			.SetVolumeAsync(resolved,
				(float)(Math.Clamp(percent, MinVolumePercent, MaxVolumePercent) / 100),
				cancellationToken)
			.ConfigureAwait(false);

		return applied ? VariableWriteResult.Applied() : VariableWriteResult.Unavailable();
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

		if (!_notifications.IsSupported)
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
		=> Task.FromResult(IssueResolution.Failed(AppStrings.Integrations.Issues.CannotResolveAutomatically()));

	private static string Now(string format) => DateTime.Now.ToString(format, CultureInfo.InvariantCulture);

	private static int? RoundPercent(double? value)
		=> value is { } percent ? (int)Math.Round(percent) : null;

	private static int? RamUsagePercent(MemoryInfo? memory)
		=> memory is { TotalBytes: > 0 } info ? (int)Math.Round(info.UsedBytes * 100.0 / info.TotalBytes) : null;

	private static double? ToGigabytes(long? bytes)
		=> bytes is { } value ? Math.Round(value / BytesPerGigabyte, 1) : null;

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
