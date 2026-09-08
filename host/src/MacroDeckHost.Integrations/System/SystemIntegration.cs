using System.Globalization;
using System.Threading.Channels;
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

		Variables = BuildVariables(_metrics.GpuCount);

		Actions =
		[
			new LaunchApplicationActionDefinition(_applications),
			new OpenWebsiteActionDefinition(_applications),
			new OpenFileActionDefinition(_applications),
			new OpenFolderActionDefinition(_applications),
			new KillApplicationActionDefinition(_applications),
			new IncreaseVolumeActionDefinition(_volume),
			new DecreaseVolumeActionDefinition(_volume),
			new MuteVolumeActionDefinition(_volume),
			new SetVolumeActionDefinition(_volume),
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

	public IReadOnlyList<VariableDefinition> Variables { get; }

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
		yield return VariableDefinition.Eager(
			$"system_gpu_{index}_usage_percent", VariableType.Numeric, 0, TimeSpan.FromSeconds(3))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.GpuUsageIndexed(index: label),
				Unit = PercentUnit,
				SemanticKind = VariableSemanticKinds.Percentage
			};
		yield return VariableDefinition.Eager(
			$"system_gpu_{index}_name", VariableType.Text, refreshInterval: TimeSpan.FromMinutes(5))
			with
			{
				DisplayName = AppStrings.Integrations.System.Variables.GpuNameIndexed(index: label)
			};
	}

	public Task InitializeAsync(IIntegrationContext context)
	{
		_variableAccessor.Current = context.Variables;

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

		return Task.CompletedTask;
	}

	public async Task ShutdownAsync()
	{
		FocusedApplicationSnapshot.Current.Changed -= OnFocusChanged;
		_focusChannel?.Writer.TryComplete();

		if (_focusWriterLoop is { } loop)
		{
			await loop;
		}

		_focusWriterLoop = null;
	}

	private void OnFocusChanged(FocusedAppInfo? info) => _focusChannel?.Writer.TryWrite(info);

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
			VolumePercentId when _volume.IsSupported =>
				await _volume.GetVolumeAsync(cancellationToken) is { } volume
					? VariableReading.Of((int)Math.Round(volume * 100), MinVolumePercent, MaxVolumePercent, 1)
					: VariableReading.Unavailable,
			"system-muted" when _volume.IsSupported =>
				VariableReading.Of(await _volume.GetMuteAsync(cancellationToken)),
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
		if (!string.Equals(localId, VolumePercentId, StringComparison.Ordinal))
		{
			return VariableWriteResult.NotWritable();
		}

		if (!_volume.IsSupported)
		{
			return VariableWriteResult.Unavailable();
		}

		if (!TryReadNumber(value, out var percent))
		{
			return VariableWriteResult.InvalidValue();
		}

		await _volume
			.SetVolumeAsync((float)(Math.Clamp(percent, MinVolumePercent, MaxVolumePercent) / 100), cancellationToken)
			.ConfigureAwait(false);

		return VariableWriteResult.Applied();
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
