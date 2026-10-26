using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Integrations.AutoHotkey.Widgets;
using MacroDeckHost.Integrations.Ui;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed partial class AutoHotkeyScriptsPageTests
{
	private string _root = null!;
	private InMemoryStorage _storage = null!;
	private AutoHotkeyScriptStore _store = null!;
	private FakeLauncher _launcher = null!;
	private AutoHotkeyProcessManager _manager = null!;
	private FakeResources _resources = null!;
	private FakeOrigin _origin = null!;
	private AutoHotkeyInstallation _installation = null!;
	private AutoHotkeyScriptsPage? _page;
	private UiViewSession? _session;
	private TestPageHost _pageHost = null!;

	[SetUp]
	public async Task SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "ahk-page-tests", Guid.NewGuid().ToString("N"));
		_storage = new InMemoryStorage();
		_store = new AutoHotkeyScriptStore(_storage, TimeProvider.System, Logger.None);
		await _store.LoadAsync();
		_launcher = new FakeLauncher { OnStart = process => process.Ready() };
		_installation = new AutoHotkeyInstallation(AutoHotkeyInstallationStatus.Valid,
			Path.Combine(_root, "AutoHotkey64.exe"),
			_root,
			"2.0.30");
		_manager = new AutoHotkeyProcessManager(new AutoHotkeyRuntime
		{
			Launcher = _launcher,
			Folders = new AutoHotkeyRunFolders(_root, Logger.None),
			Store = _store,
			Installation = () => _installation,
			Settings = () => AutoHotkeySettings.Default,
			Time = TimeProvider.System,
			Logger = Logger.None
		});
		_resources = new FakeResources();
		_origin = new FakeOrigin();
	}

	[TearDown]
	public async Task TearDown()
	{
		if (_session is not null)
		{
			await _session.DisposeAsync();
		}

		foreach (var process in _launcher.Started)
		{
			process.Exit(0);
		}

		await _manager.DisposeAsync();
		_launcher.Dispose();
		_store.Dispose();
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, true);
		}
	}

	[Test]
	public async Task Selecting_a_script_serves_its_source_only_as_an_unguessable_text_resource()
	{
		var script = await _store.CreateAsync("Mute", "Send \"^m\"");
		var host = Open();

		await SelectAsync(host, script.Id);

		var editor = host.SingleByType(UiConfigPrimitives.Code);
		var name = _page!.ResourceName!;
		Assert.Multiple(() =>
		{
			Assert.That(editor.HasProperty("value"), Is.False);
			Assert.That(editor.Text("language"), Is.EqualTo("autohotkey"));
			Assert.That(editor.Text("variableInsertFormat"), Is.EqualTo("MacroDeck.GetVar(\"{name}\")"));
			Assert.That(editor.Property("contentResource")?.GetProperty("resourceId").GetString(),
				Is.EqualTo(FakeResources.IdOf(name)));
			Assert.That(name, Does.Match("^ahk-[0-9a-f]{32}$"));
			Assert.That(Encoding.UTF8.GetString(_resources.Registered[name]), Is.EqualTo("Send \"^m\""));
			Assert.That(host.ToCanonicalJson(), Does.Not.Contain("Send"));
		});
	}

	[Test]
	public async Task An_uploaded_edit_is_saved_exactly_and_republished_under_a_new_name()
	{
		var script = await _store.CreateAsync("Edit", "old");
		var host = Open();
		await SelectAsync(host, script.Id);
		var firstName = _page!.ResourceName!;
		var uploaded = Encoding.UTF8.GetBytes("new\r\nline\twith ü");

		await UploadAsync(host, uploaded);
		Activate(host, "save");
		await host.SettleAsync();

		var secondName = _page.ResourceName!;
		var stored = await _store.ReadSourceAsync(script.Id);
		Assert.Multiple(() =>
		{
			Assert.That(stored.Text, Is.EqualTo("new\r\nline\twith ü"));
			Assert.That(secondName, Is.Not.EqualTo(firstName));
			Assert.That(_resources.Registered[secondName], Is.EqualTo(uploaded));
			Assert.That(_resources.Removed, Does.Contain(firstName));
		});
	}

	[Test]
	public async Task A_failed_upload_withholds_save_until_the_next_upload_succeeds()
	{
		var script = await _store.CreateAsync("Edit", "old");
		var host = Open();
		await SelectAsync(host, script.Id);

		Dispatch(host.SingleByType(UiConfigPrimitives.Code).Id, UiConfigEvents.ContentUploadFailed, null);
		await host.SettleAsync();
		var withheld = FindByKey(host, "save") is null;
		await UploadAsync(host, Encoding.UTF8.GetBytes("fixed"));

		Assert.Multiple(() =>
		{
			Assert.That(withheld, Is.True);
			Assert.That(FindByKey(host, "save"), Is.Not.Null);
			Assert.That(_page!.Draft, Is.EqualTo("fixed"));
		});
	}

	[Test]
	public async Task An_upload_that_is_no_longer_held_leaves_the_draft_alone()
	{
		var script = await _store.CreateAsync("Edit", "old");
		var host = Open();
		await SelectAsync(host, script.Id);

		Dispatch(host.SingleByType(UiConfigPrimitives.Code).Id,
			UiConfigEvents.ContentUploaded,
			new { uploadId = "expired", length = 3, contentHash = "sha256:00" });
		await host.SettleAsync();

		Assert.That(_page!.Draft, Is.EqualTo("old"));
	}

	[Test]
	public async Task New_creates_a_script_from_the_dialog_and_delete_removes_it_and_its_resource()
	{
		var host = Open();

		Activate(host, "new");
		FindByKey(host, "edit-name")!.Change("Hotkeys");
		FindByKey(host, "edit-description")!.Change("Mutes Discord");
		Activate(host, "edit-save");
		await host.SettleAsync();
		var created = _store.Scripts.Single();
		var subtext = FindByKey(host, "description")?.Text("text");
		var dialogClosed = FindByKey(host, "edit-dialog") is null;
		var name = _page!.ResourceName!;
		Activate(host, "delete");
		await host.SettleAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(created.Name, Is.EqualTo("Hotkeys"));
			Assert.That(created.Description, Is.EqualTo("Mutes Discord"));
			Assert.That(subtext, Is.EqualTo("Mutes Discord"));
			Assert.That(dialogClosed, Is.True);
			Assert.That(_store.Scripts, Is.Empty);
			Assert.That(_resources.Removed, Does.Contain(name));
			Assert.That(host.ByType(UiConfigPrimitives.Code), Is.Empty);
			Assert.That(await _storage.ReadAsync(AutoHotkeyScriptStore.SourceKey(created.Id)),
				Is.EqualTo(MacroDeck.Sdk.Storage.IntegrationStorageReadResult.Missing));
		});
	}

	[Test]
	public async Task Rename_edits_name_and_description_in_the_dialog_and_rejects_an_empty_name()
	{
		var script = await _store.CreateAsync("Old name", "x");
		var host = Open();
		await SelectAsync(host, script.Id);

		Activate(host, "rename");
		var prefilled = FindByKey(host, "edit-name")?.Text("value");
		var requiredOnOpen = FindByKey(host, "edit-name")?.Flag("required");
		var errorOnOpen = FindByKey(host, "edit-status-banner") is not null;
		FindByKey(host, "edit-name")!.Change("  ");
		Activate(host, "edit-save");
		await host.SettleAsync();
		var emptyNameError = FindByKey(host, "edit-status-banner") is not null;
		FindByKey(host, "edit-name")!.Change("New name");
		FindByKey(host, "edit-description")!.Change("Does things");
		Activate(host, "edit-save");
		await host.SettleAsync();

		var renamed = _store.Find(script.Id)!;
		Assert.Multiple(() =>
		{
			Assert.That(prefilled, Is.EqualTo("Old name"));
			Assert.That(requiredOnOpen, Is.Not.True);
			Assert.That(errorOnOpen, Is.False);
			Assert.That(emptyNameError, Is.True);
			Assert.That(renamed.Name, Is.EqualTo("New name"));
			Assert.That(renamed.Description, Is.EqualTo("Does things"));
			Assert.That(FindByKey(host, "edit-dialog"), Is.Null);
			Assert.That(FindByKey(host, "description")?.Text("text"), Is.EqualTo("Does things"));
			Assert.That(FindByKey(host, "name")?.Text("text"), Is.EqualTo("New name"));
			Assert.That(FindByKey(host, "name")?.HasProperty("badge"), Is.False);
		});
	}

	[Test]
	public async Task Import_reads_the_chosen_file_and_names_the_script_after_it()
	{
		Directory.CreateDirectory(_root);
		var file = Path.Combine(_root, "Window tools.ahk");
		await File.WriteAllBytesAsync(file, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("WinActivate \"x\"")]);
		var host = Open();

		Activate(host, "import-open");
		FindByKey(host, "import-file")!.Change(file);
		Activate(host, "import");
		await host.SettleAsync();

		var imported = _store.Scripts.Single();
		await Assert.MultipleAsync(async () =>
		{
			Assert.That(imported.Name, Is.EqualTo("Window tools"));
			Assert.That((await _store.ReadSourceAsync(imported.Id)).Text, Is.EqualTo("WinActivate \"x\""));
			Assert.That(_page!.SelectedId, Is.EqualTo(imported.Id));
		});
	}

	[Test]
	public async Task Read_only_library_and_unreadable_source_are_shown_and_block_changes()
	{
		var script = await _store.CreateAsync("Broken", "x");
		_storage.MakeUnreadable(AutoHotkeyScriptStore.SourceKey(script.Id));
		var host = Open();
		await SelectAsync(host, script.Id);
		var sourceBanner = FindByKey(host, "source-read-only");
		var save = FindByKey(host, "save");
		await _session!.DisposeAsync();
		_session = null;

		_storage.MakeUnreadable(AutoHotkeyScriptStore.IndexKey);
		await _store.LoadAsync();
		var readOnlyHost = Open();

		Assert.Multiple(() =>
		{
			Assert.That(sourceBanner, Is.Not.Null);
			Assert.That(save, Is.Null);
			Assert.That(FindByKey(readOnlyHost, "read-only"), Is.Not.Null);
			Assert.That(FindByKey(readOnlyHost, "new"), Is.Null);
		});
	}

	[Test]
	public async Task Without_autohotkey_the_page_offers_the_download_page()
	{
		_installation = AutoHotkeyInstallation.NotFound;
		var host = Open();

		Dispatch(FindByKey(host, "download")!.Id, UiConfigEvents.Activate, null);
		await host.SettleAsync();

		Assert.That(_origin.OpenedUrls, Is.EqualTo(new[] { new Uri("https://www.autohotkey.com/download/") }));
	}

	[Test]
	public async Task The_run_panel_follows_a_run_started_from_the_editor()
	{
		var script = await _store.CreateAsync("Prints", "FileAppend \"hello\", \"*\"");
		_launcher.OnStart = process =>
		{
			process.Ready();
			_ = Task.Run(async () =>
			{
				await Task.Delay(50);
				process.OutputPipe.Write("hello from ahk\n");
				await Task.Delay(50);
				process.Exit(4);
			});
		};
		var host = Open();
		await SelectAsync(host, script.Id);

		Dispatch(FindByKey(host, "run")!.Id, UiConfigEvents.Activate, null);
		await host.SettleAsync();
		await WaitUntilAsync(() => _manager.LatestRun(script.Id)?.State == AutoHotkeyRunState.Exited);
		await WaitUntilAsync(() => FindByKey(host, "run-output")?.Text("text")?.Contains("hello from ahk") == true);

		Assert.Multiple(() =>
		{
			Assert.That(_launcher.Started, Has.Count.EqualTo(1));
			Assert.That(FindByKey(host, "run-output")?.Text("text"), Does.Contain("hello from ahk"));
			Assert.That(FindByKey(host, "run-duration"), Is.Not.Null);
		});
	}

	[TestCase("en", "de-DE", @"\A\d+\.\d\z")]
	[TestCase("de", "en-US", @"\A\d+,\d\z")]
	public async Task The_run_duration_uses_the_number_format_of_the_page_culture_not_the_host(
		string pageCulture,
		string hostCulture,
		string expected)
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(hostCulture);
		try
		{
			var script = await _store.CreateAsync("Quick", "ExitApp");
			_launcher.OnStart = process =>
			{
				process.Ready();
				_ = Task.Run(() => process.Exit(0));
			};
			var host = Open(pageCulture);
			await SelectAsync(host, script.Id);

			Dispatch(FindByKey(host, "run")!.Id, UiConfigEvents.Activate, null);
			await host.SettleAsync();
			await WaitUntilAsync(() => _manager.LatestRun(script.Id)?.State == AutoHotkeyRunState.Exited);
			await WaitUntilAsync(() => FindByKey(host, "run-duration") is not null);

			var seconds = FindByKey(host, "run-duration")!.Property("value")!.Value
				.GetProperty("$localized")
				.GetProperty("arguments")
				.GetProperty("seconds")
				.GetString();
			Assert.That(seconds, Does.Match(expected));
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	[Test]
	public async Task A_cleared_editor_saves_an_empty_script_and_shows_empty_text()
	{
		var script = await _store.CreateAsync("Clear me", "MsgBox 1");
		var host = Open();
		await SelectAsync(host, script.Id);

		await UploadAsync(host, []);
		Activate(host, "save");
		await host.SettleAsync();

		var shown = _resources.Registered[_page!.ResourceName!];
		await Assert.MultipleAsync(async () =>
		{
			Assert.That((await _store.ReadSourceAsync(script.Id)).Text, Is.Empty);
			Assert.That(shown, Is.EqualTo(Encoding.UTF8.GetPreamble()));
			Assert.That(new StreamReader(new MemoryStream(shown), Encoding.UTF8).ReadToEnd(), Is.Empty);
		});
	}

	[Test]
	public async Task The_page_follows_a_new_library_and_process_manager_with_its_unsaved_draft()
	{
		var script = await _store.CreateAsync("Kept", "old");
		var host = Open();
		await SelectAsync(host, script.Id);
		var uploaded = Encoding.UTF8.GetBytes("unsaved draft");
		await UploadAsync(host, uploaded);
		var oldName = _page!.ResourceName!;
		var oldResources = _resources;

		await using var restarted = await RestartRuntimeAsync();
		var added = await restarted.Store.CreateAsync("Added after restart", "x");
		await WaitUntilAsync(() => FindByKey(host, "row-" + added.Id.ToString("N")) is not null);
		Dispatch(FindByKey(host, "run")!.Id, UiConfigEvents.Activate, null);
		await WaitUntilAsync(() => restarted.Launcher.Started.Count == 1);

		Assert.Multiple(() =>
		{
			Assert.That(restarted.Launcher.Started, Has.Count.EqualTo(1));
			Assert.That(File.ReadAllText(restarted.Launcher.Started[0].Info.Arguments[^1]),
				Is.EqualTo("unsaved draft"));
			Assert.That(_resources.Registered[_page.ResourceName!], Is.EqualTo(uploaded));
			Assert.That(oldResources.Removed, Does.Contain(oldName));
		});
	}

	[Test]
	public async Task Run_after_an_edit_queued_behind_other_work_runs_the_new_draft()
	{
		var script = await _store.CreateAsync("Queued", "old");
		FakeProcess? check = null;
		_launcher.OnStart = process =>
		{
			if (process.Info.Arguments.Contains("/Validate"))
			{
				check = process;
				return;
			}

			process.Ready();
		};
		var host = Open();
		await SelectAsync(host, script.Id);
		var uploadId = Guid.NewGuid().ToString("N");
		_origin.Uploads[uploadId] = Encoding.UTF8.GetBytes("new draft");

		Dispatch(FindByKey(host, "check")!.Id, UiConfigEvents.Activate, null);
		await WaitUntilAsync(() => check is not null);
		Dispatch(host.SingleByType(UiConfigPrimitives.Code).Id,
			UiConfigEvents.ContentUploaded,
			new { uploadId, length = 9, contentHash = "sha256:00" });
		Dispatch(FindByKey(host, "run")!.Id, UiConfigEvents.Activate, null);
		check!.Exit(0);
		await WaitUntilAsync(() => _launcher.Started.Count == 2);

		Assert.That(File.ReadAllText(_launcher.Started[1].Info.Arguments[^1]), Is.EqualTo("new draft"));
	}

	[Test]
	public async Task A_selected_script_opens_in_the_detail_with_the_syntax_result_next_to_its_editor()
	{
		var script = await _store.CreateAsync("Broken", "x := (");
		_launcher.OnStart = process =>
		{
			process.ErrorPipe.Write($"{process.Info.Arguments[^1]} (1) : ==> Missing \")\"\n");
			process.Exit(2);
		};
		var host = Open();
		await SelectAsync(host, script.Id);

		Activate(host, "check");
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(host.SingleByType(UiConfigPrimitives.Split).Children.Select(child => child.Type),
				Is.All.EqualTo(UiConfigPrimitives.Stack));
			Assert.That(host.ByType(UiConfigPrimitives.ListItem).Single().Flag("selected"), Is.True);
			Assert.That(FindByKey(host, "run")?.Text("variant"), Is.EqualTo("secondary"));
			Assert.That(FindByKey(host, "delete")?.Text("variant"), Is.EqualTo("danger"));
			Assert.That(host.ByType(UiConfigPrimitives.Menu), Is.Empty);
			Assert.That(FindByKey(host, "save")?.Text("variant"), Is.EqualTo("primary"));
			Assert.That(FindByKey(host, "footer")?.Text("align"), Is.EqualTo("end"));
			Assert.That(FindByKey(host, "settings")?.Text("variant"), Is.EqualTo("card"));
			Assert.That(FindByKey(host, "script-status-banner"), Is.Not.Null);
			Assert.That(FindByKey(host, "status-banner"), Is.Null);
			Assert.That(FindByKey(host, "script-status-detail")?.Text("text"), Does.Contain("Missing"));
		});
	}

	[Test]
	public async Task The_detail_shows_an_empty_state_until_a_script_is_selected()
	{
		var emptyHost = Open();
		var withoutScripts = emptyHost.SingleByType(UiConfigPrimitives.EmptyState);
		var createButton = FindByKey(emptyHost, "empty-new");
		await _session!.DisposeAsync();
		_session = null;

		var script = await _store.CreateAsync("Listed", "x");
		var host = Open();
		var beforeSelection = host.ByType(UiConfigPrimitives.EmptyState).Count;
		await SelectAsync(host, script.Id);

		Assert.Multiple(() =>
		{
			Assert.That(withoutScripts, Is.Not.Null);
			Assert.That(createButton, Is.Not.Null);
			Assert.That(beforeSelection, Is.EqualTo(1));
			Assert.That(FindByKey(host, "empty-new"), Is.Null);
			Assert.That(host.ByType(UiConfigPrimitives.EmptyState), Is.Empty);
			Assert.That(host.ByType(UiConfigPrimitives.Code), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_without_a_file_shows_the_error_in_the_import_dialog()
	{
		var host = Open();

		Activate(host, "import-open");
		Activate(host, "import");
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(FindByKey(host, "import-status-banner"), Is.Not.Null);
			Assert.That(FindByKey(host, "import-dialog"), Is.Not.Null);
			Assert.That(FindByKey(host, "status-banner"), Is.Null);
		});
	}

	[Test]
	public async Task A_failure_after_the_import_dialog_closed_shows_on_the_page()
	{
		Directory.CreateDirectory(_root);
		var file = Path.Combine(_root, "Imported.ahk");
		await File.WriteAllTextAsync(file, "x := 1");
		var host = Open();
		_resources.FailRegistrations = true;

		Activate(host, "import-open");
		FindByKey(host, "import-file")!.Change(file);
		Activate(host, "import");
		await host.SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_store.Scripts, Has.Count.EqualTo(1));
			Assert.That(FindByKey(host, "import-dialog"), Is.Null);
			Assert.That(FindByKey(host, "status-banner"), Is.Not.Null);
		});
	}

	[Test]
	public async Task A_failed_export_shows_the_error_in_the_export_dialog_until_it_closes()
	{
		var script = await _store.CreateAsync("Export me", "x");
		var host = Open();
		await SelectAsync(host, script.Id);

		Activate(host, "export");
		FindByKey(host, "export-folder")!.Change(Path.Combine(_root, "missing-folder"));
		Activate(host, "export-confirm");
		await host.SettleAsync();
		var shownInDialog = FindByKey(host, "export-status-banner") is not null;
		var pageBanner = FindByKey(host, "status-banner");
		Activate(host, "export-cancel");
		Activate(host, "export");

		Assert.Multiple(() =>
		{
			Assert.That(shownInDialog, Is.True);
			Assert.That(pageBanner, Is.Null);
			Assert.That(FindByKey(host, "export-status-banner"), Is.Null);
		});
	}

	[Test]
	public async Task Closing_the_page_removes_its_resource()
	{
		var script = await _store.CreateAsync("Any", "x");
		var host = Open();
		await SelectAsync(host, script.Id);
		var name = _page!.ResourceName!;

		await _session!.DisposeAsync();
		_session = null;

		Assert.That(_resources.Removed, Does.Contain(name));
	}

	private UiTestHost Open(string culture = "en")
	{
		_pageHost = new TestPageHost
		{
			Library = _store, Runs = _manager, Resources = _resources, Installation = _installation
		};
		_page = new AutoHotkeyScriptsPage(_pageHost, culture, Logger.None);
		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Page,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>()
		};
		var view = new UiView(surface, _page.Build());
		_session = new UiViewSession(view, _page.DisposeAsync);
		_page.Session = _session;
		return UiTestHost.Render(view);
	}

	private async Task<RestartedRuntime> RestartRuntimeAsync()
	{
		await _manager.ShutdownAsync();
		var store = new AutoHotkeyScriptStore(_storage, TimeProvider.System, Logger.None);
		await store.LoadAsync();
		var launcher = new FakeLauncher { OnStart = process => process.Ready() };
		var manager = new AutoHotkeyProcessManager(new AutoHotkeyRuntime
		{
			Launcher = launcher,
			Folders = new AutoHotkeyRunFolders(_root, Logger.None),
			Store = store,
			Installation = () => _installation,
			Settings = () => AutoHotkeySettings.Default,
			Time = TimeProvider.System,
			Logger = Logger.None
		});
		_resources = new FakeResources();
		_pageHost.Library = store;
		_pageHost.Runs = manager;
		_pageHost.Resources = _resources;
		_pageHost.Raise();
		await WaitUntilAsync(() => _page!.ResourceName is { } name && _resources.Registered.ContainsKey(name));
		return new RestartedRuntime(store, launcher, manager);
	}

	private static async Task SelectAsync(UiTestHost host, Guid scriptId)
	{
		var select = host.ByType(UiConfigPrimitives.ListItem)
			.SingleOrDefault(node => node.Id.Contains(scriptId.ToString("N"), StringComparison.Ordinal));
		Assert.That(select, Is.Not.Null, host.Describe);
		select!.Activate();
		await host.SettleAsync();
	}

	private async Task UploadAsync(UiTestHost host, byte[] content)
	{
		var uploadId = Guid.NewGuid().ToString("N");
		_origin.Uploads[uploadId] = content;
		Dispatch(host.SingleByType(UiConfigPrimitives.Code).Id,
			UiConfigEvents.ContentUploaded,
			new
			{
				uploadId,
				length = content.Length,
				contentHash = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(content))
			});
		await host.SettleAsync();
	}

	private void Dispatch(string nodeId, string name, object? data)
	{
		_session!.Dispatch(new UiEvent
			{
				NodeId = nodeId,
				Name = name,
				Data = data is null ? null : JsonSerializer.SerializeToElement(data)
			},
			_origin);
	}

	private static void Activate(UiTestHost host, string key)
	{
		var node = FindByKey(host, key)
			?? throw new AssertionException($"No node {key} in{Environment.NewLine}{host.Describe()}");
		node.Activate();
	}

	private static UiTestNode? FindByKey(UiTestHost host, string key)
	{
		return AllNodes(host.Root).FirstOrDefault(node => LastSegment().Match(node.Id).Value == key);
	}

	private static IEnumerable<UiTestNode> AllNodes(UiTestNode node)
	{
		yield return node;
		foreach (var child in node.Children.SelectMany(AllNodes))
		{
			yield return child;
		}
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(20);
		}
	}

	[GeneratedRegex(@"[^/.:]+\z")]
	private static partial Regex LastSegment();
}

internal sealed record RestartedRuntime(
	AutoHotkeyScriptStore Store,
	FakeLauncher Launcher,
	AutoHotkeyProcessManager Manager) : IAsyncDisposable
{
	public async ValueTask DisposeAsync()
	{
		foreach (var process in Launcher.Started)
		{
			process.Exit(0);
		}

		await Manager.DisposeAsync();
		Launcher.Dispose();
		Store.Dispose();
	}
}
