using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.State;
using MacroDeckHost.Platform.Focus;
using MacroDeckHost.Platform.Lock;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
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
		"send-notification", "show-notification", "run-command",
		"lock-computer", "sleep", "hibernate", "restart", "shut-down"
	];

	private static readonly string[] _expectedVariableNames =
	[
		"system_volume_percent", "system_muted", "system_input_volume_percent", "system_input_muted",
		"system_cpu_usage_percent", "system_ram_usage_percent",
		"system_ram_used_gb", "system_ram_total_gb", "system_cpu_name", "system_pc_name", "system_os",
		"system_date", "system_time", "system_datetime", "system_timestamp_unix",
		"system_day_of_week", "system_hour", "system_minute", "system_locked",
		"system_gpu_0_usage_percent", "system_gpu_0_name"
	];

	private static SignalForwardingVariableApi Forwarding(IVariableRefreshSignal signal)
	{
		return new SignalForwardingVariableApi(new RecordingVariableApi(), SystemIntegration.IntegrationId, signal);
	}

	private static SystemIntegration Create(
		FakeVolumeService? volume = null,
		bool applicationsSupported = true,
		FakeSystemMetricsService? metrics = null,
		bool powerSupported = true,
		FakeLockStateReader? lockStateReader = null,
		FakeFocusedApplicationWatcher? focusWatcher = null)
	{
		return new SystemIntegration(new FakeApplicationService { IsSupported = applicationsSupported },
			volume ?? new FakeVolumeService(),
			metrics ?? new FakeSystemMetricsService(),
			new FakePowerService(powerSupported),
			lockStateReader ?? new FakeLockStateReader(),
			() => focusWatcher ?? new FakeFocusedApplicationWatcher());
	}

	[Test]
	public void Actions_that_reach_outside_macro_deck_declare_their_risk_category()
	{
		using var integration = Create();

		var categories = integration.Actions.ToDictionary(action => action.Id, action => action.RiskCategory);

		Assert.Multiple(() =>
		{
			Assert.That(categories["run-command"], Is.EqualTo(ActionRiskCategory.Programs));
			Assert.That(categories["launch-application"], Is.EqualTo(ActionRiskCategory.Programs));
			Assert.That(categories["kill-application"], Is.EqualTo(ActionRiskCategory.Programs));
			Assert.That(categories["open-file"], Is.EqualTo(ActionRiskCategory.Files));
			Assert.That(categories["open-folder"], Is.EqualTo(ActionRiskCategory.Files));
			Assert.That(categories["shut-down"], Is.EqualTo(ActionRiskCategory.Power));
			Assert.That(categories["restart"], Is.EqualTo(ActionRiskCategory.Power));
			Assert.That(categories["sleep"], Is.EqualTo(ActionRiskCategory.Power));
			Assert.That(categories["lock-computer"], Is.EqualTo(ActionRiskCategory.None));
			Assert.That(categories["set-volume"], Is.EqualTo(ActionRiskCategory.None));
		});
	}

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
		var integration = Create(new FakeVolumeService(false));

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
		var metrics = new FakeSystemMetricsService(gpuCount: 2)
		{
			GpuUsageByIndex =
			{
				[0] = 11,
				[1] = 72
			},
			GpuNameByIndex =
			{
				[0] = "Discrete",
				[1] = "Integrated"
			}
		};
		var integration = Create(metrics: metrics);

		await Assert.MultipleAsync(async () =>
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

		await Assert.MultipleAsync(async () =>
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
		var integration = Create(metrics: new FakeSystemMetricsService(false));

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
		var integration = Create(new FakeVolumeService(false),
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
		var integration = Create(new FakeVolumeService(false));
		await integration.InitializeAsync(new FakeIntegrationContext(Forwarding(new VariableRefreshSignal()),
			new RecordingDesktopNotifier(false)));

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues.Select(i => i.Severity),
			Has.All.EqualTo(IntegrationIssueSeverity.Warning));
		Assert.That(issues, Has.Count.EqualTo(2));
	}

	[Test]
	public async Task
		GetIssuesAsync_reports_notifications_unavailable_when_the_context_cannot_show_desktop_notifications()
	{
		var integration = Create();
		await integration.InitializeAsync(new FakeIntegrationContext(Forwarding(new VariableRefreshSignal()),
			new RecordingDesktopNotifier(false)));

		var issues = await integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Is.EqualTo(new[] { "notifications-unavailable" }));
	}

	[Test]
	public async Task GetIssuesAsync_reports_no_notifications_issue_before_initialization()
	{
		var integration = Create();

		Assert.That(await integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task Send_notification_shows_through_the_contexts_desktop_notifications()
	{
		var integration = Create();
		var desktop = new RecordingDesktopNotifier();
		await integration.InitializeAsync(new FakeIntegrationContext(Forwarding(new VariableRefreshSignal()), desktop));
		var action = integration.Actions.Single(candidate => candidate.Id == "send-notification");

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["title"] = "Build finished", ["message"] = "All green" }
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(desktop.Shown, Is.EqualTo(new[] { ("Build finished", "All green") }));
		});
	}

	[Test]
	public async Task Send_notification_before_initialization_fails_as_unavailable()
	{
		var integration = Create();
		var action = integration.Actions.Single(candidate => candidate.Id == "send-notification");

		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object> { ["title"] = "Build finished", ["message"] = "All green" }
			});

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
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
		var integration = Create(metrics: new FakeSystemMetricsService(false, false));

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
	public async Task The_locked_variable_follows_the_lock_state_the_integration_reads_itself()
	{
		var reader = new FakeLockStateReader { IsSupported = true, Locked = false };
		var integration = Create(lockStateReader: reader);

		var beforeLocking = (await integration.ReadAsync("system-locked", CancellationToken.None)).Value;
		reader.Locked = true;
		var afterLocking = (await integration.ReadAsync("system-locked", CancellationToken.None)).Value;

		Assert.Multiple(() =>
		{
			Assert.That(beforeLocking, Is.EqualTo(false));
			Assert.That(afterLocking, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task ReadAsync_returns_unavailable_for_locked_when_the_reader_is_unsupported()
	{
		var integration = Create(lockStateReader: new FakeLockStateReader { IsSupported = false, Locked = true });

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
		var watcher = new FakeFocusedApplicationWatcher();
		watcher.Publish(new FocusedAppInfo(4242, "/usr/bin/foo", "procfoo", "com.example.foo"));
		var integration = Create(focusWatcher: watcher);

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
	public async Task InitializeAsync_creates_the_focus_variables_so_they_exist_before_any_focus_change()
	{
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
		var watcher = new FakeFocusedApplicationWatcher();
		var integration = Create(focusWatcher: watcher);
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

			watcher.Publish(new FocusedAppInfo(99,
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
		var watcher = new FakeFocusedApplicationWatcher();
		watcher.Publish(new FocusedAppInfo(1, "/usr/bin/foo", "procfoo", "com.example.foo"));
		var integration = Create(focusWatcher: watcher);
		var api = new RecordingVariableApi();
		var seeded = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		api.OnSetValue = (name, value) =>
		{
			if (name == "system_focused_app_bundle_id" && Equals(value, "com.example.foo"))
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

			watcher.Publish(new FocusedAppInfo(2, null, null, null));

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
	public async Task Shutdown_releases_the_focus_watcher_the_integration_started()
	{
		var watcher = new FakeFocusedApplicationWatcher();
		var integration = Create(focusWatcher: watcher);

		await integration.InitializeAsync(new FakeIntegrationContext(new RecordingVariableApi()));
		await integration.ShutdownAsync();

		Assert.That(watcher.Disposed, Is.True);
	}

	[Test]
	public async Task A_volume_change_asks_for_the_volume_and_mute_variables_only()
	{
		var volume = new FakeVolumeService();
		var signal = new VariableRefreshSignal();
		var integration = Create(volume);

		try
		{
			await integration.InitializeAsync(new FakeIntegrationContext(Forwarding(signal)));

			volume.RaiseChanged();

			Assert.Multiple(() =>
			{
				Assert.That(signal.DrainDefinitionsFor(SystemIntegration.IntegrationId, VariableRefreshLane.Eager),
					Is.EquivalentTo(new[] { "system-volume-percent", "system-muted" }));
				Assert.That(signal.DrainEagerRefreshRequested(SystemIntegration.IntegrationId), Is.False);
			});
		}
		finally
		{
			await integration.ShutdownAsync();
		}
	}

	[Test]
	public async Task After_shutdown_a_volume_change_asks_for_nothing_even_after_a_repeated_initialization()
	{
		var volume = new FakeVolumeService();
		var signal = new VariableRefreshSignal();
		var integration = Create(volume);

		await integration.InitializeAsync(new FakeIntegrationContext(Forwarding(signal)));
		await integration.InitializeAsync(new FakeIntegrationContext(Forwarding(signal)));
		await integration.ShutdownAsync();

		volume.RaiseChanged();

		Assert.That(signal.DrainDefinitionsFor(SystemIntegration.IntegrationId, VariableRefreshLane.Eager), Is.Empty);
	}

	private sealed class RecordingDesktopNotifier(bool supported = true) : IDesktopNotifier
	{
		public List<(string Title, string Message)> Shown { get; } = [];

		public bool IsSupported => supported;

		public Task ShowAsync(string title, string message, CancellationToken cancellationToken = default)
		{
			Shown.Add((title, message));
			return Task.CompletedTask;
		}
	}

	private sealed class FakeLockStateReader : ILockStateReader
	{
		public bool IsSupported { get; init; } = true;

		public string? UnsupportedReason => IsSupported ? null : "not supported in this test";

		public bool? Locked { get; set; }

		public bool? IsLocked()
		{
			return Locked;
		}
	}

	private sealed class FakeFocusedApplicationWatcher : IFocusedApplicationWatcher
	{
		private readonly Channel<FocusedAppInfo> _focus = Channel.CreateUnbounded<FocusedAppInfo>();

		public bool IsSupported => true;

		public string? UnsupportedReason => null;

		public bool Disposed { get; private set; }

		public void Publish(FocusedAppInfo info)
		{
			_focus.Writer.TryWrite(info);
		}

		public async IAsyncEnumerable<FocusedAppInfo> WatchAsync(
			[EnumeratorCancellation] CancellationToken cancellationToken)
		{
			await foreach (var info in _focus.Reader.ReadAllAsync(cancellationToken))
			{
				yield return info;
			}
		}

		public void Dispose()
		{
			Disposed = true;
		}
	}

	private sealed class FakeIntegrationContext(IVariableApi variables, IDesktopNotifier? desktopNotifications = null)
		: IIntegrationContext
	{
		public IVariableApi Variables { get; } = variables;

		public IDesktopNotifier DesktopNotifications { get; } =
			desktopNotifications ?? new RecordingDesktopNotifier();

		public IUserVariableApi UserVariables => null!;

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
