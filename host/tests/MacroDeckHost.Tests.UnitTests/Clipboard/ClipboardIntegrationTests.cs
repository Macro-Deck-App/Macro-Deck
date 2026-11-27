using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Clipboard;
using MacroDeckHost.Integrations.Clipboard.Events;
using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Integrations.Clipboard.Variables;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Clipboard;

[TestFixture]
internal sealed class ClipboardIntegrationTests
{
	private FakeClipboardContext _context = null!;
	private FakeClipboardBackend _backend = null!;
	private FakeTimeProvider _time = null!;
	private List<LogEvent> _logs = null!;
	private ClipboardIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_context = new FakeClipboardContext();
		_time = new FakeTimeProvider();
		_logs = [];
		Build(new FakeClipboardBackend());
	}

	[TearDown]
	public async Task TearDown()
	{
		await _integration.ShutdownAsync();
		_integration.Dispose();
	}

	[Test]
	public async Task Without_a_settings_entry_the_actions_work_and_nothing_is_watched()
	{
		await _integration.InitializeAsync(_context);

		var result = await RunAsync("set-text", ("text", "hello"));
		_backend.CopyElsewhere(ClipboardContentKinds.Text, "from another app");
		await IdleAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(((IConfigFlowProvider)_integration).RequiresConfiguration, Is.False);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_backend.Source.IsStarted, Is.False);
			Assert.That(_context.Published, Is.Empty);
			Assert.That(_integration.Variables, Is.Empty);
			Assert.That(_integration.DeclaredVariables.Select(definition => definition.Name),
				Is.EquivalentTo(new[]
				{
					ClipboardVariables.ContentType, ClipboardVariables.HasText, ClipboardVariables.HasImage,
					ClipboardVariables.HasFiles, ClipboardVariables.Text
				}));
			Assert.That(await ReadVariableAsync(ClipboardVariables.ContentType),
				Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public async Task Read_text_stores_the_text_and_fails_without_text_or_when_it_is_too_large()
	{
		await _integration.InitializeAsync(_context);

		_backend.Seed(ClipboardContentKinds.Text, "copied words");
		var read = await RunAsync("read-text", ("outputVariable", "snippet"));
		_backend.Seed(ClipboardContentKinds.Image);
		var noText = await RunAsync("read-text", ("outputVariable", "snippet"));
		_backend.Seed(ClipboardContentKinds.Text, new string('x', 1024 * 1024 + 1));
		var tooLarge = await RunAsync("read-text", ("outputVariable", "snippet"));

		Assert.Multiple(() =>
		{
			Assert.That(read.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_context.Written.Values["snippet"].Value, Is.EqualTo("copied words"));
			Assert.That(noText.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(tooLarge.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
		});
	}

	[Test]
	public async Task Check_content_stores_whether_the_chosen_kind_is_on_the_clipboard()
	{
		await _integration.InitializeAsync(_context);
		_backend.Seed(ClipboardContentKinds.Image);

		await RunAsync("check-content", ("kind", "image"), ("outputVariable", "isImage"));
		await RunAsync("check-content", ("kind", "text"), ("outputVariable", "isText"));

		Assert.Multiple(() =>
		{
			Assert.That(_context.Written.Values["isImage"], Is.EqualTo((VariableType.Boolean, "true")));
			Assert.That(_context.Written.Values["isText"], Is.EqualTo((VariableType.Boolean, "false")));
		});
	}

	[Test]
	public async Task Copy_files_checks_every_row_before_touching_the_clipboard_and_skips_empty_rows()
	{
		await _integration.InitializeAsync(_context);
		var folder = Directory.CreateTempSubdirectory("macrodeck-clipboard-");
		try
		{
			var file = Path.Combine(folder.FullName, "notes.txt");
			await File.WriteAllTextAsync(file, "x");
			var missing = Path.Combine(folder.FullName, "missing.txt");

			var relative = await RunAsync("copy-files", ("paths", Rows("notes.txt")));
			var notFound = await RunAsync("copy-files", ("paths", Rows(file, missing)));
			var copied = await RunAsync("copy-files", ("paths", Rows(file, "", folder.FullName)));

			Assert.Multiple(() =>
			{
				Assert.That(relative.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
				Assert.That(notFound.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
				Assert.That(copied.Status, Is.EqualTo(ActionResultStatus.Succeeded));
				Assert.That(_backend.Files, Is.EqualTo(new[] { file, folder.FullName }));
			});
		}
		finally
		{
			folder.Delete(true);
		}
	}

	[Test]
	public async Task Copy_image_refuses_a_format_the_clipboard_cannot_take()
	{
		await _integration.InitializeAsync(_context);
		var file = Path.Combine(Path.GetTempPath(), $"macrodeck-clipboard-{Guid.NewGuid():N}.svg");
		await File.WriteAllTextAsync(file, "<svg/>");
		try
		{
			var result = await RunAsync("copy-image", ("file", file));

			Assert.Multiple(() =>
			{
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
				Assert.That(_backend.Image, Is.Null);
			});
		}
		finally
		{
			File.Delete(file);
		}
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Only_a_change_from_another_app_fires_events_and_the_first_snapshot_fires_none(bool exactTokens)
	{
		Build(new FakeClipboardBackend(exactTokens));
		_backend.Seed(ClipboardContentKinds.Text, "before");
		_context.Configure(true, false);
		_context.Bind(FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "copy"));
		await _integration.InitializeAsync(_context);
		var afterStart = _context.Published.Count;

		await RunAsync("set-text", ("text", "my copy"));
		_backend.NotifyChange();
		await IdleAsync();
		var afterOwnWrite = _context.Published.Count;

		_backend.CopyElsewhere(ClipboardContentKinds.Image);
		await IdleAsync();
		_backend.CopyElsewhere(ClipboardContentKinds.Text, "your copy");
		await IdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(afterStart, Is.Zero);
			Assert.That(afterOwnWrite, Is.Zero);
			Assert.That(_context.PublishedIds,
				Is.EqualTo(new[]
				{
					ClipboardEventIds.ContentChanged, ClipboardEventIds.ContentTypeChanged,
					ClipboardEventIds.ContentChanged, ClipboardEventIds.TextMatched,
					ClipboardEventIds.ContentTypeChanged
				}));
			var last = _context.Published.Last().Payload;
			Assert.That(last[ClipboardEventPayload.ContentType], Is.EqualTo("text"));
			Assert.That(last[ClipboardEventPayload.PreviousContentType], Is.EqualTo("image"));
			var changed = _context.Published.ElementAt(2).Payload;
			Assert.That(changed[ClipboardEventPayload.Text], Is.EqualTo("your copy"));
		});
	}

	[Test]
	public async Task A_matching_text_from_another_app_after_the_own_write_window_is_not_suppressed()
	{
		Build(new FakeClipboardBackend(false));
		_backend.Seed(ClipboardContentKinds.Text, "earlier");
		_context.Configure(true, false);
		await _integration.InitializeAsync(_context);

		await RunAsync("set-text", ("text", "same"));
		_time.Now += TimeSpan.FromSeconds(3);
		_backend.CopyElsewhere(ClipboardContentKinds.Text, "same");
		await IdleAsync();

		Assert.That(_context.PublishedIds, Is.EqualTo(new[] { ClipboardEventIds.ContentChanged }));
	}

	[Test]
	public async Task Text_matched_fires_once_for_each_matching_pattern_of_an_equals_binding()
	{
		_context.Configure(true, false);
		_context.Bind(FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "^https?://"),
			FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "^https?://"),
			FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "(?i)EXAMPLE"),
			FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "^ftp://"),
			FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "([unclosed"),
			FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "https", "!="));
		await _integration.InitializeAsync(_context);

		_backend.CopyElsewhere(ClipboardContentKinds.Text, "https://example.org/page");
		await IdleAsync();

		var matched = _context.Published.Where(published => published.EventId == ClipboardEventIds.TextMatched)
			.Select(published => published.Payload)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(matched.Select(payload => payload[ClipboardEventPayload.Pattern]),
				Is.EqualTo(new[] { "^https?://", "(?i)EXAMPLE" }));
			Assert.That(matched.Select(payload => payload[ClipboardEventPayload.Match]),
				Is.EqualTo(new[] { "https://", "example" }));
			Assert.That(matched.Select(payload => payload[ClipboardEventPayload.Text]),
				Is.All.EqualTo("https://example.org/page"));
		});
	}

	[Test]
	public async Task Without_text_bindings_or_the_text_variable_the_text_is_never_read()
	{
		_context.Configure(true, false);
		await _integration.InitializeAsync(_context);

		_backend.CopyElsewhere(ClipboardContentKinds.Text, "private");
		await IdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_backend.TextReads, Is.Zero);
			Assert.That(_context.Published.First(published => published.EventId == ClipboardEventIds.ContentChanged)
					.Payload
					.ContainsKey(ClipboardEventPayload.Text),
				Is.False);
		});
	}

	[Test]
	public async Task The_text_variable_exists_only_while_both_toggles_are_on_and_goes_away_with_its_text()
	{
		_backend.Seed(ClipboardContentKinds.Text, "secret");
		_context.Configure(true, true);
		await _integration.InitializeAsync(_context);
		var declarationChanges = _context.OwnVariables.DeclarationChanges;
		var withText = _integration.Variables.Select(definition => definition.Name).ToList();
		var textWhileOn = await ReadVariableAsync(ClipboardVariables.Text);
		_context.OwnVariables.Add(ClipboardVariables.Text, VariableType.Text);
		_context.OwnVariables.Add(ClipboardVariables.HasText, VariableType.Boolean);

		_context.Configure(true, false);
		await _integration.ReloadEntriesAsync();
		var withoutText = _integration.Variables.Select(definition => definition.Name).ToList();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(withText, Does.Contain(ClipboardVariables.Text));
			Assert.That(textWhileOn, Is.EqualTo(VariableReading.Of("secret")));
			Assert.That(withoutText, Does.Not.Contain(ClipboardVariables.Text));
			Assert.That(withoutText, Does.Contain(ClipboardVariables.HasText));
			Assert.That(await ReadVariableAsync(ClipboardVariables.Text), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(_integration.Monitor.Snapshot?.Text, Is.Null);
			Assert.That(_context.OwnVariables.Names, Is.EqualTo(new[] { ClipboardVariables.HasText }));
			Assert.That(_context.OwnVariables.DeclarationChanges, Is.EqualTo(declarationChanges + 1));
		});
	}

	[Test]
	public async Task Turning_watching_off_stops_events_and_removes_every_clipboard_variable()
	{
		_context.Configure(true, false);
		await _integration.InitializeAsync(_context);
		_context.OwnVariables.Add(ClipboardVariables.ContentType, VariableType.Text);

		_context.Configure(false, false);
		await _integration.ReloadEntriesAsync();
		_backend.CopyElsewhere(ClipboardContentKinds.Text, "later");
		await IdleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_backend.Source.IsStarted, Is.False);
			Assert.That(_context.Published, Is.Empty);
			Assert.That(_integration.Variables, Is.Empty);
			Assert.That(_context.OwnVariables.Names, Is.Empty);
		});
	}

	[Test]
	public async Task State_variables_follow_the_clipboard_while_watching()
	{
		_context.Configure(true, false);
		await _integration.InitializeAsync(_context);

		_backend.CopyElsewhere(ClipboardContentKinds.Files | ClipboardContentKinds.Text, "names");
		await IdleAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await ReadVariableAsync(ClipboardVariables.ContentType),
				Is.EqualTo(VariableReading.Of("files")));
			Assert.That(await ReadVariableAsync(ClipboardVariables.HasFiles), Is.EqualTo(VariableReading.Of(true)));
			Assert.That(await ReadVariableAsync(ClipboardVariables.HasImage), Is.EqualTo(VariableReading.Of(false)));
		});
	}

	[Test]
	public async Task An_unavailable_clipboard_fails_actions_and_reports_an_issue()
	{
		var backend = new FakeClipboardBackend
		{
			Availability = ClipboardAvailability.Unavailable(ClipboardUnavailableReason.ToolMissing, "wl-clipboard")
		};
		Build(backend);
		_context.Configure(true, false);
		await _integration.InitializeAsync(_context);

		var result = await RunAsync("clear");
		var issues = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(result.ErrorMessage.Localized?.Arguments.Values.Select(value => value?.ToString()),
				Does.Contain("wl-clipboard"));
			Assert.That(issues.Select(issue => issue.Id),
				Is.EqualTo(new[] { ClipboardIntegration.UnavailableIssueId }));
			Assert.That(backend.Source.IsStarted, Is.False);
		});
	}

	[Test]
	public async Task A_desktop_that_cannot_be_watched_reports_an_issue_only_while_watching_is_on()
	{
		_context.Configure(true, false);
		await _integration.InitializeAsync(_context);
		_backend.Source.ReportWatchUnsupported();
		var whileOn = await _integration.GetIssuesAsync();

		_context.Configure(false, false);
		await _integration.ReloadEntriesAsync();
		var whileOff = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(whileOn.Select(issue => issue.Id),
				Is.EqualTo(new[] { ClipboardIntegration.WatchUnsupportedIssueId }));
			Assert.That(whileOff, Is.Empty);
		});
	}

	[Test]
	public async Task A_desktop_that_cannot_be_watched_keeps_no_clipboard_state_in_its_variables()
	{
		_backend.Seed(ClipboardContentKinds.Text, "secret");
		_context.Configure(true, true);
		await _integration.InitializeAsync(_context);
		var textBefore = await ReadVariableAsync(ClipboardVariables.Text);

		_backend.Source.ReportWatchUnsupported();
		await IdleAsync();
		_context.Configure(true, false);
		await _integration.ReloadEntriesAsync();
		_context.Configure(true, true);
		await _integration.ReloadEntriesAsync();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(textBefore, Is.EqualTo(VariableReading.Of("secret")));
			Assert.That(await ReadVariableAsync(ClipboardVariables.Text), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(await ReadVariableAsync(ClipboardVariables.ContentType),
				Is.EqualTo(VariableReading.Unavailable));
			Assert.That(await ReadVariableAsync(ClipboardVariables.HasText), Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public async Task Clipboard_content_never_reaches_the_log()
	{
		const string secret = "hunter2-very-secret";
		_context.Configure(true, true);
		_context.Bind(FakeClipboardContext.Binding(ClipboardEventIds.TextMatched, "hunter"));
		await _integration.InitializeAsync(_context);

		await RunAsync("set-text", ("text", secret));
		_backend.NotifyChange();
		_backend.CopyElsewhere(ClipboardContentKinds.Text, secret);
		await IdleAsync();
		await RunAsync("read-text", ("outputVariable", "out"));

		var rendered = _logs.Select(log
				=> log.RenderMessage(CultureInfo.InvariantCulture) + string.Join(' ', log.Properties.Values))
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(_logs, Is.Not.Empty);
			Assert.That(rendered, Has.None.Contains("hunter2"));
		});
	}

	[Test]
	public void A_linux_session_needs_a_display_or_a_wayland_display()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ClipboardBackendFactory.HasDesktopSession(_ => null), Is.False);
			Assert.That(ClipboardBackendFactory.HasDesktopSession(name => name == "DISPLAY" ? ":0" : null), Is.True);
			Assert.That(
				ClipboardBackendFactory.HasDesktopSession(name => name == "WAYLAND_DISPLAY" ? "wayland-0" : null),
				Is.True);
		});
	}

	private void Build(FakeClipboardBackend backend)
	{
		_integration?.Dispose();
		_backend = backend;
		var logger = new LoggerConfiguration().MinimumLevel.Verbose()
			.WriteTo.Sink(new DelegatingLogSink(log => _logs.Add(log)))
			.CreateLogger();
		_integration = new ClipboardIntegration(backend, _time, logger);
	}

	private Task<ActionResult> RunAsync(string actionId, params (string Name, object Value)[] parameters)
	{
		var action = _integration.Actions.Single(candidate => candidate.Id == actionId);
		return action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = parameters.ToDictionary(parameter => parameter.Name, parameter => parameter.Value)
			});
	}

	private static JsonElement Rows(params string[] rows)
	{
		return JsonSerializer.SerializeToElement(rows);
	}

	private Task IdleAsync()
	{
		return _integration.Monitor.WhenIdleAsync();
	}

	private async Task<VariableReading> ReadVariableAsync(string name)
	{
		var id = _integration.DeclaredVariables.Single(definition => definition.Name == name).ResolvedId!;
		return await _integration.ReadAsync(id);
	}
}
