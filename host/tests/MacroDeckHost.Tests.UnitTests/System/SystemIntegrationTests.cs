using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Focus;
using MacroDeckHost.Integrations.System.Lock;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemIntegrationTests
{
	private static readonly string[] _expectedActionIds =
	[
		"launch-application", "open-website", "open-file", "open-folder", "kill-application",
		"increase-volume", "decrease-volume", "mute-volume", "set-volume",
		"send-notification", "run-command",
		"lock-computer", "sleep", "hibernate", "restart", "shut-down"
	];

	private static readonly string[] _expectedVariableNames =
	[
		"system_volume_percent", "system_muted", "system_cpu_usage_percent", "system_ram_usage_percent",
		"system_ram_used_gb", "system_ram_total_gb", "system_cpu_name", "system_pc_name", "system_os",
		"system_date", "system_time", "system_datetime", "system_timestamp_unix",
		"system_day_of_week", "system_hour", "system_minute", "system_locked",
		"system_gpu_0_usage_percent", "system_gpu_0_name"
	];

	[TearDown]
	public void TearDown()
	{
		FocusedApplicationSnapshot.Current.Set(null);
		LockStateSnapshot.Current.Set(null);
	}

	private static SystemIntegration Create(
		FakeVolumeService? volume = null,
		bool applicationsSupported = true,
		bool notificationsSupported = true,
		FakeSystemMetricsService? metrics = null,
		bool powerSupported = true,
		FakeLockStateReader? lockStateReader = null)
		=> new(new FakeApplicationService { IsSupported = applicationsSupported },
			volume ?? new FakeVolumeService(),
			new FakeNotificationService { IsSupported = notificationsSupported },
			metrics ?? new FakeSystemMetricsService(),
			new FakePowerService(powerSupported),
			lockStateReader ?? new FakeLockStateReader());

	[Test]
	public void Exposes_all_sixteen_actions_with_stable_ids()
	{
		var integration = Create();

		var ids = integration.Actions.Select(a => a.Id).ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(integration.Id, Is.EqualTo("app.macro-deck.system"));
			Assert.That(ids, Is.EquivalentTo(_expectedActionIds));
		});
	}

	[Test]
	public void Provides_volume_mute_metric_machine_info_and_datetime_variables()
	{
		var integration = Create();

		Assert.That(integration.Variables.Select(v => v.Name),
			Is.EquivalentTo(_expectedVariableNames));
	}

	[Test]
	public async Task ReadAsync_returns_scaled_volume_and_mute_with_the_volumes_own_range()
	{
		var integration = Create(new FakeVolumeService { Volume = 0.5f, Muted = true });

		var volume = await integration.ReadAsync("system-volume-percent", CancellationToken.None);
		var muted = (await integration.ReadAsync("system-muted", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(volume.Value, Is.EqualTo(50));
			Assert.That(muted, Is.EqualTo(true));

			// The range travels with the reading rather than with a separate slider contract, so whatever
			// writes the volume - a Slider widget, a script, the REST API - gets it without asking again.
			Assert.That(volume.Min, Is.EqualTo(0));
			Assert.That(volume.Max, Is.EqualTo(100));
			Assert.That(volume.Step, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task ReadAsync_returns_null_when_volume_unsupported()
	{
		var integration = Create(new FakeVolumeService(supported: false));

		Assert.That((await integration.ReadAsync("system-volume-percent", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public async Task ReadAsync_returns_null_when_volume_currently_unavailable()
	{
		var integration = Create(new FakeVolumeService { Volume = null, Muted = null });

		var volume = (await integration.ReadAsync("system-volume-percent", CancellationToken.None)).Value;
		var muted = (await integration.ReadAsync("system-muted", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(volume, Is.Null);
			Assert.That(muted, Is.Null);
		});
	}

	[Test]
	public async Task ReadAsync_returns_rounded_metric_values()
	{
		var metrics = new FakeSystemMetricsService
		{
			CpuUsage = 37.4,
			Memory = new MemoryInfo(32L * 1024 * 1024 * 1024, 20L * 1024 * 1024 * 1024),
			GpuUsage = 55.6
		};
		var integration = Create(metrics: metrics);

		var cpu = (await integration.ReadAsync("system-cpu-usage-percent", CancellationToken.None)).Value;
		var ramUsage = (await integration.ReadAsync("system-ram-usage-percent", CancellationToken.None)).Value;
		var ramUsed = (await integration.ReadAsync("system-ram-used-gb", CancellationToken.None)).Value;
		var ramTotal = (await integration.ReadAsync("system-ram-total-gb", CancellationToken.None)).Value;
		var gpu = (await integration.ReadAsync("system-gpu-0-usage-percent", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(cpu, Is.EqualTo(37));
			Assert.That(ramUsage, Is.EqualTo(38));
			Assert.That(ramUsed, Is.EqualTo(12.0));
			Assert.That(ramTotal, Is.EqualTo(32.0));
			Assert.That(gpu, Is.EqualTo(56));
		});
	}

	[Test]
	public void Every_detected_gpu_gets_its_own_variables()
	{
		var integration = Create(metrics: new FakeSystemMetricsService(gpuCount: 2));

		Assert.That(integration.Variables.Select(v => v.Name),
			Is.SupersetOf(new[]
			{
				"system_gpu_0_usage_percent", "system_gpu_0_name",
				"system_gpu_1_usage_percent", "system_gpu_1_name"
			}));
	}

	[Test]
	public async Task Indexed_gpu_variables_read_their_own_gpu()
	{
		var metrics = new FakeSystemMetricsService(gpuCount: 2);
		metrics.GpuUsageByIndex[0] = 11;
		metrics.GpuUsageByIndex[1] = 72;
		metrics.GpuNameByIndex[0] = "Discrete";
		metrics.GpuNameByIndex[1] = "Integrated";
		var integration = Create(metrics: metrics);

		Assert.Multiple(async () =>
		{
			Assert.That((await integration.ReadAsync("system-gpu-0-usage-percent", CancellationToken.None)).Value,
				Is.EqualTo(11));
			Assert.That((await integration.ReadAsync("system-gpu-1-usage-percent", CancellationToken.None)).Value,
				Is.EqualTo(72));
			Assert.That((await integration.ReadAsync("system-gpu-0-name", CancellationToken.None)).Value,
				Is.EqualTo("Discrete"));
			Assert.That((await integration.ReadAsync("system-gpu-1-name", CancellationToken.None)).Value,
				Is.EqualTo("Integrated"));
		});
	}

	[Test]
	public async Task A_machine_without_a_gpu_declares_no_gpu_variables()
	{
		var integration = Create(metrics: new FakeSystemMetricsService(gpuSupported: false));

		Assert.Multiple(async () =>
		{
			Assert.That(integration.Variables.Select(v => v.Name), Has.None.StartsWith("system_gpu"));
			Assert.That((await integration.ReadAsync("system-gpu-0-usage-percent", CancellationToken.None)).Value,
				Is.Null);
			Assert.That((await integration.ReadAsync("system-gpu-0-name", CancellationToken.None)).Value, Is.Null);
		});
	}

	[Test]
	public async Task ReadAsync_returns_null_metrics_when_readings_unavailable()
	{
		var integration = Create(metrics: new FakeSystemMetricsService(supported: false));

		var names = new[]
		{
			"system-cpu-usage-percent", "system-ram-usage-percent", "system-ram-used-gb",
			"system-ram-total-gb", "system-gpu-0-usage-percent"
		};
		foreach (var name in names)
		{
			Assert.That((await integration.ReadAsync(name, CancellationToken.None)).Value, Is.Null, name);
		}
	}

	[Test]
	public async Task ReadAsync_returns_metrics_even_when_volume_unsupported()
	{
		var integration = Create(new FakeVolumeService(supported: false),
			metrics: new FakeSystemMetricsService { CpuUsage = 12.0 });

		Assert.That((await integration.ReadAsync("system-cpu-usage-percent", CancellationToken.None)).Value,
			Is.EqualTo(12));
	}

	[Test]
	public async Task ReadAsync_returns_pc_name_and_os_name()
	{
		var integration = Create();

		var pcName = (await integration.ReadAsync("system-pc-name", CancellationToken.None)).Value;
		var osName = (await integration.ReadAsync("system-os", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(pcName, Is.EqualTo(Environment.MachineName));
			Assert.That(osName, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public async Task ReadAsync_returns_gpu_name_from_metrics()
	{
		var integration = Create(metrics: new FakeSystemMetricsService { GpuName = "NVIDIA GeForce RTX 3080" });

		Assert.That((await integration.ReadAsync("system-gpu-0-name", CancellationToken.None)).Value,
			Is.EqualTo("NVIDIA GeForce RTX 3080"));
	}

	[Test]
	public async Task ReadAsync_returns_null_gpu_name_when_gpu_unsupported()
	{
		var integration = Create(metrics: new FakeSystemMetricsService(gpuSupported: false) { GpuName = "ignored" });

		Assert.That((await integration.ReadAsync("system-gpu-0-name", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public async Task ReadAsync_returns_cpu_name_matching_system_info()
	{
		var integration = Create();

		Assert.That((await integration.ReadAsync("system-cpu-name", CancellationToken.None)).Value,
			Is.EqualTo(SystemInfo.CpuName));
	}

	[Test]
	public async Task ReadAsync_returns_formatted_date_and_time_values()
	{
		var integration = Create();

		var date = (await integration.ReadAsync("system-date", CancellationToken.None)).Value;
		var time = (await integration.ReadAsync("system-time", CancellationToken.None)).Value;
		var dateTime = (await integration.ReadAsync("system-datetime", CancellationToken.None)).Value;
		var timestamp = (await integration.ReadAsync("system-timestamp-unix", CancellationToken.None)).Value;
		var dayOfWeek = (await integration.ReadAsync("system-day-of-week", CancellationToken.None)).Value;
		var hour = (await integration.ReadAsync("system-hour", CancellationToken.None)).Value;
		var minute = (await integration.ReadAsync("system-minute", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(date, Is.TypeOf<string>().And.Match(@"^\d{4}-\d{2}-\d{2}$"));
			Assert.That(time, Is.TypeOf<string>().And.Match(@"^\d{2}:\d{2}:\d{2}$"));
			Assert.That(dateTime, Is.TypeOf<string>().And.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$"));
			Assert.That(timestamp, Is.TypeOf<long>().And.GreaterThan(0L));
			Assert.That(dayOfWeek, Is.EqualTo(DateTime.Now.DayOfWeek.ToString()));
			Assert.That(hour, Is.InRange(0, 23));
			Assert.That(minute, Is.InRange(0, 59));
		});
	}

	[Test]
	public async Task ReadAsync_returns_null_for_unknown_variable()
	{
		var integration = Create();

		Assert.That((await integration.ReadAsync("system-unknown", CancellationToken.None)).Value, Is.Null);
	}

	[Test]
	public async Task GetIssuesAsync_reports_warnings_for_unavailable_backends()
	{
		var integration = Create(new FakeVolumeService(supported: false),
			notificationsSupported: false);

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues.Select(i => i.Severity),
			Has.All.EqualTo(IntegrationIssueSeverity.Warning));
		Assert.That(issues, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task GetIssuesAsync_reports_warning_when_gpu_metrics_unavailable()
	{
		var integration = Create(metrics: new FakeSystemMetricsService(gpuSupported: false));

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(issues[0].Id, Is.EqualTo("gpu-metrics-unavailable"));
			Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
		});
	}

	[Test]
	public async Task GetIssuesAsync_skips_gpu_warning_when_metrics_unsupported()
	{
		var integration = Create(metrics: new FakeSystemMetricsService(supported: false, gpuSupported: false));

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task GetIssuesAsync_reports_single_error_when_platform_unsupported()
	{
		var integration = Create(applicationsSupported: false);

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues, Has.Count.EqualTo(1));
		Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Error));
	}

	[Test]
	public async Task GetIssuesAsync_reports_warning_when_power_control_unavailable()
	{
		var integration = Create(powerSupported: false);

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(issues[0].Id, Is.EqualTo("power-control-unavailable"));
			Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
		});
	}

	[Test]
	public async Task ReadAsync_returns_literal_true_when_supported_and_locked()
	{
		var integration = Create(lockStateReader: new FakeLockStateReader { IsSupported = true, Locked = true });

		var locked = (await integration.ReadAsync("system-locked", CancellationToken.None)).Value;

		Assert.That(locked, Is.EqualTo(true));
	}

	[Test]
	public async Task ReadAsync_returns_literal_false_when_supported_and_unlocked()
	{
		var integration = Create(lockStateReader: new FakeLockStateReader { IsSupported = true, Locked = false });

		var locked = (await integration.ReadAsync("system-locked", CancellationToken.None)).Value;

		Assert.That(locked, Is.EqualTo(false));
	}

	[Test]
	public async Task ReadAsync_returns_null_for_locked_when_unsupported()
	{
		var integration = Create(lockStateReader: new FakeLockStateReader { IsSupported = false, Locked = true });

		var locked = (await integration.ReadAsync("system-locked", CancellationToken.None)).Value;

		Assert.That(locked, Is.Null);
	}

	[Test]
	public async Task ReadAsync_prefers_the_snapshot_over_the_reader_when_a_snapshot_value_is_set()
	{
		var integration = Create(lockStateReader: new FakeLockStateReader { IsSupported = true, Locked = false });
		LockStateSnapshot.Current.Set(true);

		var locked = (await integration.ReadAsync("system-locked", CancellationToken.None)).Value;

		Assert.That(locked, Is.EqualTo(true));
	}

	[Test]
	public async Task ReadAsync_still_returns_unavailable_when_unsupported_even_with_a_snapshot_value()
	{
		var integration = Create(lockStateReader: new FakeLockStateReader { IsSupported = false, Locked = true });
		LockStateSnapshot.Current.Set(true);

		var reading = await integration.ReadAsync("system-locked", CancellationToken.None);

		Assert.That(reading, Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public void ProvidedVariables_contains_exactly_one_system_locked_boolean_entry_with_a_one_second_refresh()
	{
		var integration = Create();

		var entries = integration.Variables.Where(v => v.Name == "system_locked").ToList();

		Assert.That(entries, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(entries[0].Type, Is.EqualTo(VariableType.Boolean));
			Assert.That(entries[0].RefreshInterval, Is.EqualTo(TimeSpan.FromSeconds(1)));
		});
	}

	[Test]
	public async Task ReadAsync_returns_null_for_focused_app_fields_because_they_are_pushed_not_polled()
	{
		FocusedApplicationSnapshot.Current.Set(new FocusedAppInfo(4242, "/usr/bin/foo", "procfoo", "com.example.foo"));
		var integration = Create();

		var processName = (await integration.ReadAsync("system-focused-app", CancellationToken.None)).Value;
		var path = (await integration.ReadAsync("system-focused-app-path", CancellationToken.None)).Value;
		var bundleId = (await integration.ReadAsync("system-focused-app-bundle-id", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(processName, Is.Null);
			Assert.That(path, Is.Null);
			Assert.That(bundleId, Is.Null);
		});
	}

	[Test]
	public async Task InitializeAsync_publishes_the_current_snapshot_once_so_the_variables_exist_from_startup()
	{
		FocusedApplicationSnapshot.Current.Set(null);
		var integration = Create();
		var api = new RecordingVariableApi();
		var written = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		api.OnSetValue = (name, value) =>
		{
			if (name == "system_focused_app_bundle_id")
			{
				written.TrySetResult(value);
			}
		};

		try
		{
			await integration.InitializeAsync(new FakeIntegrationContext(api));
			await written.Task.WaitAsync(TimeSpan.FromSeconds(5));

			var app = await api.GetByNameAsync("system_focused_app");
			var path = await api.GetByNameAsync("system_focused_app_path");
			var bundleId = await api.GetByNameAsync("system_focused_app_bundle_id");

			Assert.Multiple(() =>
			{
				Assert.That(app?.Value, Is.EqualTo(string.Empty));
				Assert.That(path?.Value, Is.EqualTo(string.Empty));
				Assert.That(bundleId?.Value, Is.EqualTo(string.Empty));
			});
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task Focus_changes_are_pushed_to_the_variable_api_after_initialization()
	{
		FocusedApplicationSnapshot.Current.Set(null);
		var integration = Create();
		var api = new RecordingVariableApi();
		var written = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		api.OnSetValue = (name, value) =>
		{
			if (name == "system_focused_app_bundle_id" && Equals(value, "com.example.bar"))
			{
				written.TrySetResult(value);
			}
		};

		try
		{
			await integration.InitializeAsync(new FakeIntegrationContext(api));

			FocusedApplicationSnapshot.Current.Set(new FocusedAppInfo(99,
				"/usr/bin/bar",
				"procbar",
				"com.example.bar"));

			await written.Task.WaitAsync(TimeSpan.FromSeconds(5));

			var app = await api.GetByNameAsync("system_focused_app");
			var path = await api.GetByNameAsync("system_focused_app_path");
			var bundleId = await api.GetByNameAsync("system_focused_app_bundle_id");

			Assert.Multiple(() =>
			{
				Assert.That(app?.Value, Is.EqualTo("procbar"));
				Assert.That(path?.Value, Is.EqualTo("/usr/bin/bar"));
				Assert.That(bundleId?.Value, Is.EqualTo("com.example.bar"));
			});
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task A_focus_change_to_null_fields_pushes_empty_strings_instead_of_a_stale_value()
	{
		FocusedApplicationSnapshot.Current.Set(new FocusedAppInfo(1, "/usr/bin/foo", "procfoo", "com.example.foo"));
		var integration = Create();
		var api = new RecordingVariableApi();
		var seeded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		api.OnSetValue = (name, value) =>
		{
			if (name == "system_focused_app_bundle_id")
			{
				seeded.TrySetResult(value);
			}
		};

		try
		{
			await integration.InitializeAsync(new FakeIntegrationContext(api));

			// Waited out before triggering the next change, so the coalescing capacity-1 channel cannot
			// merge the seed publish and this test's own focus change into a single write cycle.
			await seeded.Task.WaitAsync(TimeSpan.FromSeconds(5));

			var written = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
			api.OnSetValue = (name, value) =>
			{
				if (name == "system_focused_app_bundle_id")
				{
					written.TrySetResult(value);
				}
			};

			FocusedApplicationSnapshot.Current.Set(new FocusedAppInfo(2, null, null, null));

			await written.Task.WaitAsync(TimeSpan.FromSeconds(5));

			var app = await api.GetByNameAsync("system_focused_app");
			var path = await api.GetByNameAsync("system_focused_app_path");
			var bundleId = await api.GetByNameAsync("system_focused_app_bundle_id");

			Assert.Multiple(() =>
			{
				Assert.That(app?.Value, Is.EqualTo(string.Empty));
				Assert.That(path?.Value, Is.EqualTo(string.Empty));
				Assert.That(bundleId?.Value, Is.EqualTo(string.Empty));
			});
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	private sealed class FakeNotificationService : INotificationService
	{
		public bool IsSupported { get; init; } = true;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}

	private sealed class FakeLockStateReader : ILockStateReader
	{
		public bool IsSupported { get; init; } = true;

		public string? UnsupportedReason => IsSupported ? null : "not supported in this test";

		public bool? Locked { get; init; }

		public bool? IsLocked() => Locked;
	}

	private sealed class FakeIntegrationContext(IVariableApi variables) : IIntegrationContext
	{
		public IVariableApi Variables { get; } = variables;

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
