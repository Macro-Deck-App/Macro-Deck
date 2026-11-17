using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.System;
using MacroDeckHost.Integrations.System.Configuration;
using MacroDeckHost.Integrations.System.Metrics;
using MacroDeckHost.Integrations.System.Volume;
using MacroDeckHost.Platform.Applications;
using MacroDeckHost.Platform.Focus;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemIntegrationApplicationTests
{
	private const string GamePath = "/games/game";

	private readonly List<SystemIntegration> _started = [];

	[TearDown]
	public async Task TearDown()
	{
		foreach (var integration in _started)
		{
			await integration.ShutdownAsync();
			integration.Dispose();
		}

		_started.Clear();
	}

	private async Task<(SystemIntegration Integration, RecordingVariableApi Api)> StartAsync(
		FakeProcessSnapshots processes,
		FakeWindowReader windows,
		FakeSystemConfig? config = null,
		FakeWatcher? focus = null,
		FakeVolumeService? volume = null,
		FakeSystemMetricsService? metrics = null,
		bool sampleManually = false)
	{
		var integration = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			volume ?? new FakeVolumeService(),
			metrics ?? new FakeSystemMetricsService(),
			new FakePowerService(true),
			focusWatcherFactory: focus is null ? null : () => focus,
			processSnapshots: processes,
			windowReader: windows);
		var api = new RecordingVariableApi();
		await integration.InitializeAsync(new FakeSystemContext(api, config ?? new FakeSystemConfig()));
		_started.Add(integration);
		if (sampleManually)
		{
			await integration.ShutdownAsync();
		}

		return (integration, api);
	}

	private static async Task<object?> Read(SystemIntegration integration, string name)
	{
		var id = integration.Variables.Single(variable => variable.Name == name).ResolvedId!;
		return (await integration.ReadAsync(id)).Value ?? "unavailable";
	}

	[Test]
	public async Task Without_watched_applications_no_application_variables_or_window_state_issue_exist()
	{
		var processes = new FakeProcessSnapshots();
		var windows = new FakeWindowReader { Availability = ApplicationWindowAvailability.AccessibilityNotGranted };
		var (integration, _) = await StartAsync(processes, windows);

		integration.SampleApplications();
		var issues = await integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(integration.RequiresConfiguration, Is.False);
			Assert.That(integration.Variables.Select(variable => variable.Name),
				Has.None.StartsWith("system_app_"));
			Assert.That(issues.Select(issue => issue.Id), Has.None.EqualTo("window-state-unavailable"));
		});
	}

	[Test]
	public async Task A_watched_application_reports_its_processes_windows_and_most_visible_window()
	{
		var processes = new FakeProcessSnapshots();
		processes.Running.AddRange([
			new RunningProcess(10, "game", GamePath), new RunningProcess(11, "game", GamePath),
			new RunningProcess(12, "editor", null)
		]);
		var windows = new FakeWindowReader();
		windows.Windows[10] = [ApplicationWindowState.Minimized];
		windows.Windows[11] = [ApplicationWindowState.Normal, ApplicationWindowState.Maximized];
		windows.Windows[12] = [ApplicationWindowState.Fullscreen];
		var config = new FakeSystemConfig();
		config.Add("Game", "game", "game");
		var (integration, _) = await StartAsync(processes, windows, config, sampleManually: true);

		integration.SampleApplications();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await Read(integration, "system_app_game_running"), Is.EqualTo(true));
			Assert.That(await Read(integration, "system_app_game_instances"), Is.EqualTo(2));
			Assert.That(await Read(integration, "system_app_game_windows"), Is.EqualTo(3));
			Assert.That(await Read(integration, "system_app_game_window_state"), Is.EqualTo("maximized"));
		});
	}

	[Test]
	public async Task The_variables_follow_the_application_as_it_starts_minimizes_goes_fullscreen_and_quits()
	{
		var processes = new FakeProcessSnapshots();
		var windows = new FakeWindowReader();
		var config = new FakeSystemConfig();
		var entryId = config.Add("Game", "game", "game");
		var (integration, api) = await StartAsync(processes, windows, config, sampleManually: true);

		integration.SampleApplications();
		var notRunning = (await Read(integration, "system_app_game_running"),
			await Read(integration, "system_app_game_window_state"));

		processes.Running.Add(new RunningProcess(10, "game", GamePath));
		windows.Windows[10] = [ApplicationWindowState.Minimized];
		api.ClearRequests();
		integration.SampleApplications();
		var minimized = await Read(integration, "system_app_game_window_state");
		var refreshed = api.DefinitionRefreshes;

		windows.Windows[10] = [ApplicationWindowState.Fullscreen];
		integration.SampleApplications();
		var fullscreen = await Read(integration, "system_app_game_window_state");

		processes.Running.Clear();
		windows.Windows.Clear();
		integration.SampleApplications();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(notRunning, Is.EqualTo((false, "not_running")));
			Assert.That(minimized, Is.EqualTo("minimized"));
			Assert.That(refreshed, Does.Contain($"entry-{entryId:N}-window-state"));
			Assert.That(fullscreen, Is.EqualTo("fullscreen"));
			Assert.That(await Read(integration, "system_app_game_running"), Is.EqualTo(false));
			Assert.That(await Read(integration, "system_app_game_instances"), Is.EqualTo(0));
			Assert.That(await Read(integration, "system_app_game_window_state"), Is.EqualTo("not_running"));
		});
	}

	[Test]
	public async Task Without_window_access_the_window_values_are_unavailable_but_processes_still_count()
	{
		var processes = new FakeProcessSnapshots();
		processes.Running.Add(new RunningProcess(10, "game", GamePath));
		var windows = new FakeWindowReader { Availability = ApplicationWindowAvailability.Wayland };
		windows.Windows[10] = [ApplicationWindowState.Normal];
		var config = new FakeSystemConfig();
		config.Add("Game", "game", "game");
		var (integration, _) = await StartAsync(processes, windows, config, sampleManually: true);

		integration.SampleApplications();
		var issues = await integration.GetIssuesAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await Read(integration, "system_app_game_running"), Is.EqualTo(true));
			Assert.That(await Read(integration, "system_app_game_instances"), Is.EqualTo(1));
			Assert.That(await Read(integration, "system_app_game_windows"), Is.EqualTo("unavailable"));
			Assert.That(await Read(integration, "system_app_game_window_state"), Is.EqualTo("unavailable"));
			Assert.That(issues.Select(issue => issue.Id), Does.Contain("window-state-unavailable"));
		});
	}

	[Test]
	public async Task Application_variables_come_after_disks_and_before_remembered_audio_devices()
	{
		var volume = new FakeVolumeService();
		volume.Devices.Add(new AudioDevice("speakers-uid", "Speakers", AudioFlow.Output, true));
		var metrics = new FakeSystemMetricsService();
		metrics.Disks.Add(new DiskSample("/", "Macintosh HD", "apfs", 1000, 250, null));
		var config = new FakeSystemConfig();
		config.Add("Game", "game", "game");
		var (integration, _) = await StartAsync(new FakeProcessSnapshots(),
			new FakeWindowReader(),
			config,
			volume: volume,
			metrics: metrics);

		var names = integration.Variables.Select(variable => variable.Name!).ToList();
		var lastDisk = names.FindLastIndex(name => name.StartsWith("system_disk_", StringComparison.Ordinal));
		var firstApplication = names.FindIndex(name => name.StartsWith("system_app_", StringComparison.Ordinal));
		var lastApplication = names.FindLastIndex(name => name.StartsWith("system_app_", StringComparison.Ordinal));
		var firstAudio = names.FindIndex(name => name.StartsWith("system_audio_", StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(lastDisk, Is.GreaterThanOrEqualTo(0));
			Assert.That(firstAudio, Is.GreaterThanOrEqualTo(0));
			Assert.That(firstApplication, Is.GreaterThan(lastDisk));
			Assert.That(lastApplication, Is.LessThan(firstAudio));
		});
	}

	[Test]
	public async Task A_focus_change_writes_the_window_state_and_process_count_together_with_the_application()
	{
		var processes = new FakeProcessSnapshots();
		processes.Running.AddRange([
			new RunningProcess(10, "game", GamePath), new RunningProcess(11, "game", GamePath)
		]);
		var windows = new FakeWindowReader();
		windows.Windows[10] = [ApplicationWindowState.Fullscreen];
		var focus = new FakeWatcher();
		var (_, api) = await StartAsync(processes, windows, focus: focus);
		var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var app = string.Empty;
		api.OnSetValue = (name, value) =>
		{
			if (name == "system_focused_app")
			{
				app = value as string ?? string.Empty;
			}

			if (name == "system_focused_app_instances" && app == "game")
			{
				written.TrySetResult();
			}
		};

		focus.Publish(new FocusedAppInfo(10, GamePath, "game", null));
		await written.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var state = await api.GetByNameAsync("system_focused_app_window_state");
		var instances = await api.GetByNameAsync("system_focused_app_instances");
		Assert.Multiple(() =>
		{
			Assert.That(state?.Value, Is.EqualTo("fullscreen"));
			Assert.That(instances?.Value, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Without_window_access_the_focused_window_state_is_empty()
	{
		var processes = new FakeProcessSnapshots();
		processes.Running.Add(new RunningProcess(10, "game", GamePath));
		var windows = new FakeWindowReader { Availability = ApplicationWindowAvailability.AccessibilityNotGranted };
		var focus = new FakeWatcher();
		var (_, api) = await StartAsync(processes, windows, focus: focus);
		var written = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		api.OnSetValue = (name, value) =>
		{
			if (name == "system_focused_app_instances" && Equals(value, 1))
			{
				written.TrySetResult();
			}
		};

		focus.Publish(new FocusedAppInfo(10, GamePath, "game", null));
		await written.Task.WaitAsync(TimeSpan.FromSeconds(5));

		var state = await api.GetByNameAsync("system_focused_app_window_state");
		Assert.That(state?.Value, Is.EqualTo(string.Empty));
	}

	[Test]
	public async Task A_new_entry_beyond_the_limit_is_refused_while_editing_one_still_works()
	{
		var integration = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			new FakeVolumeService(),
			new FakeSystemMetricsService(),
			new FakePowerService(true));
		var siblings = Enumerable.Range(0, SystemConfigKeys.MaximumWatchedApplications)
			.Select(index => FakeSystemConfig.Stored(Guid.NewGuid(), $"App {index}", $"app_{index}"))
			.ToList();

		var added = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(),
			Title = "One more",
			Values = new Dictionary<string, JsonElement>(),
			Siblings = siblings
		});
		var edited = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = siblings[0].Id,
			Title = "App 0",
			Values = new Dictionary<string, JsonElement>(),
			Existing = siblings[0],
			Siblings = siblings.Skip(1).ToList()
		});

		Assert.Multiple(() =>
		{
			Assert.That(added.Success, Is.False);
			Assert.That(edited.Success, Is.True);
			Assert.That(edited.ReservedVariables.Select(variable => variable.Name),
				Does.Contain("system_app_app_0_window_state"));
		});
	}

	[Test]
	public async Task A_name_taken_by_another_variable_gets_a_different_variable_name()
	{
		var integration = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			new FakeVolumeService(),
			new FakeSystemMetricsService(),
			new FakePowerService(true));

		var result = await integration.PrepareEntryAsync(new ConfigEntryPreparation
		{
			EntryId = Guid.NewGuid(),
			Title = "Game",
			Values = new Dictionary<string, JsonElement>(),
			RejectedVariableNames = ["system_app_game_running"]
		});

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.ReservedVariables.Select(variable => variable.Name),
				Has.None.EqualTo("system_app_game_running"));
		});
	}

	[Test]
	public async Task Reloading_entries_before_initialization_does_nothing()
	{
		var integration = new SystemIntegration(new FakeApplicationService { IsSupported = true },
			new FakeVolumeService(),
			new FakeSystemMetricsService(),
			new FakePowerService(true));

		Assert.That(await integration.ReloadEntriesAsync(), Is.False);
	}

	[Test]
	public async Task Removing_an_entry_through_a_reload_removes_its_variables()
	{
		var config = new FakeSystemConfig();
		var entryId = config.Add("Game", "game", "game");
		var (integration, _) = await StartAsync(new FakeProcessSnapshots(), new FakeWindowReader(), config);
		var before = integration.Variables.Count(variable
			=> variable.Name!.StartsWith("system_app_game_", StringComparison.Ordinal));

		config.Remove(entryId);
		var reloaded = await integration.ReloadEntriesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.EqualTo(4));
			Assert.That(reloaded, Is.True);
			Assert.That(integration.Variables.Select(variable => variable.Name), Has.None.StartsWith("system_app_"));
		});
	}

	internal sealed class FakeProcessSnapshots : IProcessSnapshotSource
	{
		public List<RunningProcess> Running { get; } = [];

		public IReadOnlyList<RunningProcess> Capture()
		{
			lock (Running)
			{
				return [.. Running];
			}
		}
	}

	internal sealed class FakeWindowReader : IApplicationWindowReader
	{
		public ApplicationWindowAvailability Availability { get; set; } = ApplicationWindowAvailability.Available;

		public Dictionary<int, IReadOnlyList<ApplicationWindowState>> Windows { get; } = [];

		public IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> Read(IReadOnlySet<int> processIds)
		{
			lock (Windows)
			{
				return Windows.Where(entry => processIds.Contains(entry.Key))
					.ToDictionary(entry => entry.Key, entry => entry.Value);
			}
		}

		public void Dispose()
		{
		}
	}

	internal sealed class FakeWatcher : IFocusedApplicationWatcher
	{
		private readonly Channel<FocusedAppInfo> _focus = Channel.CreateUnbounded<FocusedAppInfo>();

		public bool IsSupported => true;

		public string? UnsupportedReason => null;

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
		}
	}

	internal sealed class FakeSystemConfig : IIntegrationConfig
	{
		private readonly List<ConfigEntrySnapshot> _entries = [];
		private readonly Dictionary<(Guid, string), string?> _strings = [];

		public Guid Add(string title, string application, string key)
		{
			var entryId = Guid.NewGuid();
			_entries.Add(new ConfigEntrySnapshot(entryId, title));
			_strings[(entryId, SystemConfigKeys.Application)] = application;
			_strings[(entryId, SystemConfigKeys.VariableIdentityKey)]
				= ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(title, key));
			return entryId;
		}

		public void Remove(Guid entryId)
		{
			_entries.RemoveAll(entry => entry.Id == entryId);
		}

		public static StoredConfigEntry Stored(Guid entryId, string title, string key)
		{
			return new StoredConfigEntry
			{
				Id = entryId,
				Title = title,
				Values = new Dictionary<string, JsonElement>
				{
					[SystemConfigKeys.VariableIdentityKey] = JsonSerializer.SerializeToElement(
						ConfigurationIdentityJson.Serialize(new ConfigurationIdentity(title, key)))
				}
			};
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([.. _entries]);
		}

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult(_strings.GetValueOrDefault((entryId, key)));
		}

		public Task<string?> GetSecretAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
		}

		public Task SetStringAsync(
			Guid entryId,
			string key,
			string? value,
			CancellationToken cancellationToken = default)
		{
			_strings[(entryId, key)] = value;
			return Task.CompletedTask;
		}

		public Task SetSecretAsync(
			Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class FakeSystemContext(IVariableApi variables, IIntegrationConfig config) : IIntegrationContext
	{
		public IVariableApi Variables { get; } = variables;

		public IIntegrationStorage Storage => UnavailableIntegrationStorage.Instance;

		public IUserVariableApi UserVariables => null!;

		public IIntegrationConfig Config { get; } = config;

		public IDeckNavigator Deck { get; } = new RecordingDeckNavigator();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
