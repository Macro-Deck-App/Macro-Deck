using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Storage;
using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Integrations.AutoHotkey;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Integrations.Ui;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class AutoHotkeyIntegrationTests
{
	private string _root = null!;
	private InMemoryStorage _storage = null!;
	private FakeLauncher _launcher = null!;
	private FakeMachine _machine = null!;
	private AutoHotkeyIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "ahk-integration-tests", Guid.NewGuid().ToString("N"));
		_storage = new InMemoryStorage();
		_launcher = new FakeLauncher { OnStart = process => process.Ready() };
		_machine = new FakeMachine();
		_integration = new AutoHotkeyIntegration(_machine, () => _launcher, _root, TimeProvider.System);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _integration.ShutdownAsync();
		_integration.Dispose();
		_launcher.Dispose();
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, true);
		}
	}

	[Test]
	public async Task Without_autohotkey_the_integration_reports_an_issue_with_the_download_link()
	{
		await _integration.InitializeAsync(new FakeContext(_storage));

		var issues = await _integration.GetIssuesAsync();
		var installed = await _integration.ReadAsync("autohotkey-installed");

		Assert.Multiple(() =>
		{
			Assert.That(issues.Select(issue => issue.Id), Is.EqualTo(new[] { "autohotkey-not-found" }));
			Assert.That(issues[0].HelpUrl, Is.EqualTo(new Uri("https://www.autohotkey.com/download/")));
			Assert.That(installed.Value, Is.EqualTo(false));
			Assert.That(_launcher.Started, Is.Empty);
		});
	}

	[Test]
	public async Task A_script_marked_to_start_with_macro_deck_runs_and_shows_as_running()
	{
		InstallAutoHotkey();
		var scriptId = await CreateAutostartScriptAsync("Hotkeys");
		var context = new FakeContext(_storage);

		await _integration.InitializeAsync(context);
		await WaitUntilAsync(() => context.Published.Contains("script-started"));

		var running = await _integration.ReadAsync("autohotkey-script-hotkeys-running");
		var toggle = _integration.Actions.OfType<IStateProviderActionDefinition>().Single();
		var state = await toggle.GetActionStateAsync(new Dictionary<string, object?>
				{ ["target"] = "managed", ["script"] = scriptId.ToString("D") },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_launcher.Started, Has.Count.EqualTo(1));
			Assert.That(running.Value, Is.EqualTo(true));
			Assert.That(_integration.Variables.Select(variable => variable.Name),
				Does.Contain("autohotkey_script_hotkeys_running"));
			Assert.That(state?.ActiveStateId, Is.EqualTo("running"));
		});
	}

	[Test]
	public async Task Disabling_the_integration_stops_its_scripts()
	{
		InstallAutoHotkey();
		await CreateAutostartScriptAsync("Persistent");
		var context = new FakeContext(_storage);
		await _integration.InitializeAsync(context);
		await WaitUntilAsync(() => _launcher.Started.Count == 1);

		await _integration.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_launcher.Started[0].CloseRequests, Is.EqualTo(1));
			Assert.That(_launcher.Started[0].HasExited, Is.True);
			Assert.That(_launcher.Disposed, Is.True);
			Assert.That(_integration.IsInitialized, Is.False);
		});
	}

	[Test]
	public async Task The_scripts_page_is_declared_and_only_page_surfaces_are_served()
	{
		await _integration.InitializeAsync(new FakeContext(_storage));
		var configSurface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>()
		};

		var declined = await _integration.CreateSessionAsync(
			new UiSessionRequest { Surface = configSurface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_integration.Pages.Select(page => page.Id), Is.EqualTo(new[] { "scripts" }));
			Assert.That(_integration.Surfaces.Select(surface => surface.Kind), Is.EqualTo(new[] { "page" }));
			Assert.That(declined, Is.Null);
		});
	}

	[Test]
	public async Task An_open_scripts_page_keeps_working_after_the_integration_is_reinitialised()
	{
		InstallAutoHotkey();
		await _integration.InitializeAsync(new FakeContext(_storage));
		var session = (UiViewSession)(await _integration.CreateSessionAsync(PageRequest(), CancellationToken.None))!;
		var host = UiTestHost.Render(session.View);

		await _integration.InitializeAsync(new FakeContext(_storage));
		var created = await _integration.Library!.CreateAsync("After restart", "Persistent");
		await WaitUntilAsync(() => Item(host, created.Id) is not null);
		Item(host, created.Id)!.Activate();
		await host.SettleAsync();
		await WaitUntilAsync(() => Node(host, "run", null) is not null);
		session.Dispatch(new UiEvent { NodeId = Node(host, "run", null)!.Id, Name = UiConfigEvents.Activate });
		await WaitUntilAsync(() => _launcher.Started.Count == 1);

		Assert.Multiple(() =>
		{
			Assert.That(_launcher.Started, Has.Count.EqualTo(1));
			Assert.That(_integration.Runs!.IsScriptRunning(created.Id), Is.True);
		});
		await session.DisposeAsync();
	}

	[Test]
	public async Task Start_with_macro_deck_scripts_start_once_check_again_finds_autohotkey()
	{
		await CreateAutostartScriptAsync("Hotkeys");
		await _integration.InitializeAsync(new FakeContext(_storage));
		var startedWithoutAutoHotkey = _launcher.Started.Count;

		InstallAutoHotkey();
		await _integration.ResolveIssueAsync("autohotkey-not-found");
		await WaitUntilAsync(() => _launcher.Started.Count == 1);

		Assert.Multiple(() =>
		{
			Assert.That(startedWithoutAutoHotkey, Is.Zero);
			Assert.That(_launcher.Started, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Saving_the_settings_keeps_scripts_running_and_applies_them_live()
	{
		InstallAutoHotkey();
		await CreateAutostartScriptAsync("Persistent");
		await _integration.InitializeAsync(new FakeContext(_storage));
		await WaitUntilAsync(() => _launcher.Started.Count == 1);

		var result = await _integration.CreateConfigFlow()
			.SubmitAsync("settings",
				new Dictionary<string, object?> { ["executableMode"] = "auto", ["gracePeriod"] = 1.5d },
				NoOAuthConfigFlowContext.Instance,
				CancellationToken.None);
		var reopened = await _integration.CreateConfigFlow()
			.StartAsync(NoOAuthConfigFlowContext.Instance, CancellationToken.None);

		var process = _launcher.Started.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.StoresEntry, Is.False);
			Assert.That(process.HasExited, Is.False);
			Assert.That(process.CloseRequests, Is.Zero);
			Assert.That(_integration.Settings.GracePeriod, Is.EqualTo(TimeSpan.FromMilliseconds(1500)));
			Assert.That(reopened.NextStep!.Fields.Single(field => field.Name == "gracePeriod").DefaultValue,
				Is.EqualTo(1.5d));
			Assert.That(_storage.Documents.ContainsKey("settings"), Is.True);
		});
	}

	[Test]
	public async Task The_settings_show_the_executable_in_use_a_file_picker_and_the_grace_period_in_seconds()
	{
		InstallAutoHotkey();
		var portable = Path.Combine(_root, "portable", "AutoHotkey64.exe");
		_machine.Files[portable] = new AutoHotkeyFileVersion(2, "2.1.0");
		await _integration.InitializeAsync(new FakeContext(_storage));
		await _integration.CreateConfigFlow()
			.SubmitAsync("settings",
				new Dictionary<string, object?>
				{
					["executableMode"] = "manual", ["executablePath"] = portable, ["gracePeriod"] = 2d
				},
				NoOAuthConfigFlowContext.Instance,
				CancellationToken.None);

		var step = (await _integration.CreateConfigFlow()
			.StartAsync(NoOAuthConfigFlowContext.Instance, CancellationToken.None)).NextStep!;

		var path = step.Fields.Single(field => field.Name == "executablePath");
		var grace = step.Fields.Single(field => field.Name == "gracePeriod");
		Assert.Multiple(() =>
		{
			Assert.That(step.Values.Select(value => value.Value), Is.EqualTo(new[] { portable, "2.1.0" }));
			Assert.That(path.Type, Is.EqualTo(ActionParameterType.File));
			Assert.That(path.FileExtensions, Is.EqualTo(new[] { "exe" }));
			Assert.That(path.DefaultValue, Is.EqualTo(portable));
			Assert.That(grace.Type, Is.EqualTo(ActionParameterType.Number));
			Assert.That(new[] { grace.Min, grace.Max, grace.Step }, Is.EqualTo(new double?[] { 0.5, 5, 0.5 }));
			Assert.That(grace.DefaultValue, Is.EqualTo(2d));
			Assert.That(_integration.Settings.GracePeriod, Is.EqualTo(TimeSpan.FromSeconds(2)));
		});
	}

	[Test]
	public async Task Startup_removes_running_variables_of_scripts_that_no_longer_exist()
	{
		await CreateAutostartScriptAsync("Keep");
		var context = new FakeContext(_storage);
		var kept = Handle("autohotkey_script_keep_running");
		var stale = Handle("autohotkey_script_gone_running");
		var status = Handle("autohotkey_running_scripts");
		context.Handles.AddRange([kept, stale, status]);

		await _integration.InitializeAsync(context);

		Assert.That(context.Deleted, Is.EqualTo(new[] { stale.Id }));
	}

	[Test]
	public async Task Deleting_a_script_removes_its_running_variable()
	{
		await CreateAutostartScriptAsync("Keep");
		var removedId = await CreateAutostartScriptAsync("Remove");
		var context = new FakeContext(_storage);
		var kept = Handle("autohotkey_script_keep_running");
		var removed = Handle("autohotkey_script_remove_running");
		context.Handles.AddRange([kept, removed]);
		await _integration.InitializeAsync(context);

		await _integration.Library!.DeleteAsync(removedId);
		await WaitUntilAsync(() => context.Deleted.Contains(removed.Id));

		Assert.That(context.Deleted, Is.EqualTo(new[] { removed.Id }));
	}

	private static VariableHandle Handle(string name)
	{
		return new VariableHandle(Guid.NewGuid(), name, VariableType.Boolean, false, null)
		{
			DefinitionId = name.Replace('_', '-')
		};
	}

	private static UiSessionRequest PageRequest()
	{
		return new UiSessionRequest
		{
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Page,
				SessionMode = UiSessionModes.Exclusive,
				Attributes = new Dictionary<string, JsonElement>
				{
					[UiPageSurfaceAttributes.PageId] = JsonSerializer.SerializeToElement("scripts")
				}
			},
			UiModelVersion = 1
		};
	}

	private static UiTestNode? Item(UiTestHost host, Guid scriptId)
	{
		return host.ByType(UiConfigPrimitives.ListItem)
			.FirstOrDefault(node => node.Id.Contains(scriptId.ToString("N"), StringComparison.Ordinal));
	}

	private static UiTestNode? Node(UiTestHost host, string key, Guid? scriptId)
	{
		return host.ByType(UiConfigPrimitives.Button)
			.FirstOrDefault(node => node.Id.EndsWith("." + key, StringComparison.Ordinal)
				&& (scriptId is null || node.Id.Contains(scriptId.Value.ToString("N"), StringComparison.Ordinal)));
	}

	private void InstallAutoHotkey()
	{
		var installDir = Path.Combine(_root, "AutoHotkey");
		_machine.ProgramFilesFolder = _root;
		_machine.Files[Path.Combine(installDir, "v2", "AutoHotkey64.exe")] = new AutoHotkeyFileVersion(2, "2.0.30");
	}

	private async Task<Guid> CreateAutostartScriptAsync(string name)
	{
		using var store = new AutoHotkeyScriptStore(_storage, TimeProvider.System, Logger.None);
		await store.LoadAsync();
		var script = await store.CreateAsync(name, "Persistent");
		await store.UpdateSettingsAsync(script.Id, new AutoHotkeyScriptSettings(null, true, true, null));
		return script.Id;
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}

	private sealed class FakeContext : IIntegrationContext, IIntegrationConfig, IEventPublisher, IVariableApi
	{
		private readonly List<string> _published = [];

		public FakeContext(IIntegrationStorage storage)
		{
			Storage = storage;
		}

		public IReadOnlyList<string> Published
		{
			get
			{
				lock (_published)
				{
					return [.. _published];
				}
			}
		}

		public IIntegrationStorage Storage { get; }

		public IVariableApi Variables => this;

		public IUserVariableApi UserVariables { get; } = new RecordingUserVariables();

		public IIntegrationConfig Config => this;

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => this;

		public IUserNotifier Notifications { get; } = new RecordingNotifier();

		public IUiResourceRegistry UiResources { get; } = new FakeResources();

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		{
			lock (_published)
			{
				_published.Add(eventId);
			}
		}

		public Task<IReadOnlyList<ConfigEntrySnapshot>> GetEntriesAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult<IReadOnlyList<ConfigEntrySnapshot>>([]);
		}

		public Task<string?> GetStringAsync(Guid entryId, string key, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>(null);
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
			throw new NotSupportedException();
		}

		public Task SetSecretAsync(
			Guid entryId,
			string key,
			string value,
			CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public List<VariableHandle> Handles { get; } = [];

		public List<Guid> Deleted { get; } = [];

		public Task<IReadOnlyList<VariableHandle>> GetAllAsync()
		{
			lock (Handles)
			{
				return Task.FromResult<IReadOnlyList<VariableHandle>>([.. Handles]);
			}
		}

		public Task<VariableHandle?> GetByNameAsync(string name)
		{
			return Task.FromResult<VariableHandle?>(null);
		}

		public Task<VariableHandle> CreateAsync(
			string name,
			VariableType type,
			object? initialValue = null,
			int? decimalPlaces = null,
			string? definitionId = null)
		{
			throw new NotSupportedException();
		}

		public Task SetValueAsync(Guid variableId, object? value)
		{
			throw new NotSupportedException();
		}

		public Task DeleteAsync(Guid variableId)
		{
			lock (Handles)
			{
				Deleted.Add(variableId);
				Handles.RemoveAll(handle => handle.Id == variableId);
			}

			return Task.CompletedTask;
		}
	}
}
