using System.Diagnostics;
using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using MacroDeckHost.Localization;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.AutoHotkey;

[TestFixture]
internal sealed class AutoHotkeyProcessManagerTests
{
	private static readonly ILogger _logger = Logger.None;

	private string _root = null!;
	private FakeLauncher _launcher = null!;
	private AutoHotkeyScriptStore _store = null!;
	private RecordingUserVariables _variables = null!;
	private AutoHotkeyInstallation _installation = null!;
	private AutoHotkeyProcessManager _manager = null!;

	[SetUp]
	public async Task SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "ahk-manager-tests", Guid.NewGuid().ToString("N"));
		_launcher = new FakeLauncher { OnStart = process => process.Ready() };
		_store = new AutoHotkeyScriptStore(new InMemoryStorage(), TimeProvider.System, _logger);
		await _store.LoadAsync();
		_variables = new RecordingUserVariables();
		_installation = new AutoHotkeyInstallation(AutoHotkeyInstallationStatus.Valid,
			Path.Combine(_root, "AutoHotkey64.exe"),
			_root,
			"2.0.30");
		_manager = new AutoHotkeyProcessManager(new AutoHotkeyRuntime
		{
			Launcher = _launcher,
			Folders = new AutoHotkeyRunFolders(_root, _logger),
			Store = _store,
			Installation = () => _installation,
			Settings = () => new AutoHotkeySettings(null, TimeSpan.FromMilliseconds(200)),
			Time = TimeProvider.System,
			Logger = _logger,
			UserVariables = _variables,
			ReadyTimeout = TimeSpan.FromSeconds(5)
		});
	}

	[TearDown]
	public async Task TearDown()
	{
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
	public async Task Without_autohotkey_a_run_fails_as_unavailable_and_starts_nothing()
	{
		_installation = AutoHotkeyInstallation.NotFound;
		var script = await _store.CreateAsync("Any", "x := 1");

		var result = await _manager.RunAsync(Managed(script.Id), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(_launcher.Started, Is.Empty);
		});
	}

	[Test]
	public async Task A_deleted_script_fails_as_not_found()
	{
		var result = await _manager.RunAsync(Managed(Guid.NewGuid()), CancellationToken.None);

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task An_invalid_parameter_name_fails_before_anything_starts()
	{
		var script = await _store.CreateAsync("Params", "x := 1");

		var result = await _manager.RunAsync(Managed(script.Id) with
			{
				Parameters = new Dictionary<string, string> { ["not valid"] = "1" }
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_launcher.Started, Is.Empty);
		});
	}

	[Test]
	public async Task The_script_and_its_parameters_reach_autohotkey_as_files_and_switches()
	{
		var script = await _store.CreateAsync("Mute Discord", "Send \"^m\"");
		string? prelude = null;
		string? main = null;
		_launcher.OnStart = process =>
		{
			prelude = File.ReadAllText(process.PreludePath);
			main = File.ReadAllText(process.Info.Arguments[^1]);
			process.Ready();
		};

		var result = await _manager.RunAsync(Managed(script.Id) with
			{
				Parameters = new Dictionary<string, string> { ["target"] = "a\"b" }
			},
			CancellationToken.None);

		var started = _launcher.Started.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(started.Info.Arguments.Take(2), Is.EqualTo(new[] { "/ErrorStdOut=UTF-8", "/include" }));
			Assert.That(Path.GetFileName(started.Info.Arguments[^1]), Is.EqualTo("Mute Discord.ahk"));
			Assert.That(main, Is.EqualTo("Send \"^m\""));
			Assert.That(prelude, Does.Contain("MacroDeck.Params[\"target\"] := \"a`\"b\""));
			Assert.That(started.Info.WorkingDirectory, Does.StartWith(_root));
		});
	}

	[TestCase(AutoHotkeyRunPolicy.Ignore, 1, AutoHotkeyRunStatus.Ignored)]
	[TestCase(AutoHotkeyRunPolicy.StartAnother, 2, AutoHotkeyRunStatus.Started)]
	[TestCase(AutoHotkeyRunPolicy.Fail, 1, AutoHotkeyRunStatus.Failed)]
	[TestCase(AutoHotkeyRunPolicy.Restart, 2, AutoHotkeyRunStatus.Started)]
	public async Task A_second_run_of_a_running_script_follows_the_policy(
		AutoHotkeyRunPolicy policy,
		int expectedProcesses,
		AutoHotkeyRunStatus expectedStatus)
	{
		var script = await _store.CreateAsync("Persistent", "Persistent");
		await _manager.RunAsync(Managed(script.Id), CancellationToken.None);

		var second = await _manager.RunAsync(Managed(script.Id) with { Policy = policy }, CancellationToken.None);

		var first = _launcher.Started[0];
		Assert.Multiple(() =>
		{
			Assert.That(second.Status, Is.EqualTo(expectedStatus));
			Assert.That(_launcher.Started, Has.Count.EqualTo(expectedProcesses));
			Assert.That(first.HasExited, Is.EqualTo(policy == AutoHotkeyRunPolicy.Restart));
		});
	}

	[Test]
	public async Task Inline_code_is_one_instance_per_owner_and_code()
	{
		var widgetA = new UserVariableContext("widget-a");
		var widgetB = new UserVariableContext("widget-b");

		await _manager.RunAsync(Inline("Persistent", widgetA), CancellationToken.None);
		var sameOwner = await _manager.RunAsync(Inline("Persistent", widgetA), CancellationToken.None);
		var otherOwner = await _manager.RunAsync(Inline("Persistent", widgetB), CancellationToken.None);
		var otherCode = await _manager.RunAsync(Inline("Persistent ; changed", widgetA), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(sameOwner.Status, Is.EqualTo(AutoHotkeyRunStatus.Ignored));
			Assert.That(otherOwner.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(otherCode.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(_launcher.Started, Has.Count.EqualTo(3));
		});
	}

	[Test]
	public async Task Waiting_reports_a_non_zero_exit_code_with_the_captured_output()
	{
		var script = await _store.CreateAsync("Exits", "ExitApp 3");
		_launcher.OnStart = process =>
		{
			process.Ready();
			_ = Task.Run(async () =>
			{
				await Task.Delay(50);
				process.OutputPipe.Write("hello\n");
				process.Send("{\"t\":\"result\",\"text\":\"partial\"}");
				await Task.Delay(50);
				process.Exit(3);
			});
		};

		var result = await _manager.RunAsync(Managed(script.Id) with { Wait = true }, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(result.ExitCode, Is.EqualTo(3));
			Assert.That(result.Output, Does.Contain("hello"));
			Assert.That(result.Result, Is.EqualTo("partial"));
			Assert.That(_manager.LastExitCode, Is.EqualTo(3));
		});
	}

	[Test]
	public async Task A_timed_out_run_is_asked_to_close_and_then_ended()
	{
		var script = await _store.CreateAsync("Stubborn", "Persistent");
		_launcher.OnStart = process =>
		{
			process.HonoursClose = false;
			process.Ready();
		};

		var result = await _manager.RunAsync(Managed(script.Id) with
			{
				Wait = true, Timeout = TimeSpan.FromMilliseconds(200)
			},
			CancellationToken.None);

		var process = _launcher.Started.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
			Assert.That(process.CloseRequests, Is.EqualTo(1));
			Assert.That(process.Killed, Is.True);
		});
	}

	[Test]
	public async Task A_load_error_fails_the_run_with_the_line_of_the_main_script()
	{
		var script = await _store.CreateAsync("Broken", "x := (");
		_launcher.OnStart = process =>
		{
			process.ErrorPipe.Write($"{process.Info.Arguments[^1]} (1) : ==> Missing \")\"\n");
			process.Exit(2);
		};

		var result = await _manager.RunAsync(Managed(script.Id), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(_manager.LatestRun(script.Id)?.Error?.Line, Is.EqualTo(1));
			Assert.That(_manager.LatestRun(script.Id)?.State, Is.EqualTo(AutoHotkeyRunState.FailedToStart));
		});
	}

	[Test]
	public async Task Get_variable_requests_are_answered_in_the_context_of_the_action()
	{
		_variables.Values["volume"] = "42";
		var context = new UserVariableContext("widget-1", "script-1", "automation-1");
		var script = await _store.CreateAsync("Reads", "v := MacroDeck.GetVar(\"volume\")");
		_launcher.OnStart = process =>
		{
			process.Ready();
			process.Send("{\"t\":\"get\",\"id\":1,\"name\":\"volume\"}");
			process.Send("{\"t\":\"get\",\"id\":2,\"name\":\"missing\"}");
		};

		await _manager.RunAsync(Managed(script.Id) with { VariableContext = context }, CancellationToken.None);
		var process = _launcher.Started.Single();
		await WaitUntilAsync(() => process.InputWriter.Text.Count(character => character == '\n') == 2);

		Assert.Multiple(() =>
		{
			Assert.That(process.InputWriter.Text, Does.StartWith("1\tok\t42\n"));
			Assert.That(process.InputWriter.Text, Does.Contain("2\terror\t"));
			Assert.That(_variables.Reads.Select(read => read.Context).Distinct(), Is.EqualTo(new[] { context }));
		});
	}

	[Test]
	public async Task Stop_affects_only_runs_of_the_given_identity_and_optionally_only_the_newest()
	{
		var first = await _store.CreateAsync("First", "Persistent");
		var second = await _store.CreateAsync("Second", "Persistent");
		var startAnother = Managed(first.Id) with { Policy = AutoHotkeyRunPolicy.StartAnother };
		await _manager.RunAsync(startAnother, CancellationToken.None);
		await _manager.RunAsync(startAnother, CancellationToken.None);
		await _manager.RunAsync(Managed(second.Id), CancellationToken.None);

		var newestOnly = await _manager.StopAsync(AutoHotkeyIdentity.Managed(first.Id), false);
		var remaining = await _manager.StopAsync(AutoHotkeyIdentity.Managed(first.Id), true);

		Assert.Multiple(() =>
		{
			Assert.That(newestOnly, Is.EqualTo(1));
			Assert.That(remaining, Is.EqualTo(1));
			Assert.That(_manager.IsScriptRunning(first.Id), Is.False);
			Assert.That(_manager.IsScriptRunning(second.Id), Is.True);
			Assert.That(_launcher.Started[1].CloseRequests, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_finished_run_leaves_no_run_folder_behind()
	{
		var script = await _store.CreateAsync("Quick", "x := 1");
		_launcher.OnStart = process =>
		{
			process.Ready();
			_ = Task.Run(() => process.Exit(0));
		};

		await _manager.RunAsync(Managed(script.Id) with { Wait = true }, CancellationToken.None);

		Assert.That(Directory.EnumerateDirectories(_root), Is.Empty);
	}

	[Test]
	public async Task Cancelling_a_waiting_action_stops_its_run()
	{
		var script = await _store.CreateAsync("Long", "Persistent");
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		Assert.That(async () => await _manager.RunAsync(Managed(script.Id) with { Wait = true }, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
		await WaitUntilAsync(() => _launcher.Started.Single().HasExited);

		Assert.That(_manager.IsScriptRunning(script.Id), Is.False);
	}

	[Test]
	public async Task No_more_than_the_concurrency_cap_run_at_once()
	{
		for (var index = 0; index < AutoHotkeyProcessManager.MaxConcurrentRuns; index++)
		{
			await _manager.RunAsync(Inline($"Persistent ; {index}", new UserVariableContext()), CancellationToken.None);
		}

		var over = await _manager.RunAsync(Inline("Persistent ; over", new UserVariableContext()),
			CancellationToken.None);

		Assert.That(over.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
	}

	[Test]
	public async Task Shutdown_stops_every_run_and_releases_the_launcher()
	{
		var script = await _store.CreateAsync("Persistent", "Persistent");
		await _manager.RunAsync(Managed(script.Id), CancellationToken.None);
		_launcher.Started[0].HonoursClose = false;

		await _manager.ShutdownAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_launcher.Started[0].CloseRequests, Is.EqualTo(1));
			Assert.That(_launcher.Started[0].Killed, Is.True);
			Assert.That(_launcher.Disposed, Is.True);
		});
	}

	[Test]
	public async Task Error_output_without_newlines_is_read_in_bounded_chunks()
	{
		var script = await _store.CreateAsync("Floods", "Persistent");
		await _manager.RunAsync(Managed(script.Id), CancellationToken.None);
		var process = _launcher.Started.Single();

		process.ErrorPipe.Write(new string('e', 200_000));
		await WaitUntilAsync(() => _manager.LatestRun(script.Id) is { } latest
			&& _manager.FullOutput(latest.RunId)?.Length == OutputRing.DefaultCapacity);

		var output = _manager.LatestRun(script.Id)!.Output;
		var full = _manager.FullOutput(_manager.LatestRun(script.Id)!.RunId)!;
		Assert.Multiple(() =>
		{
			Assert.That(output, Is.EqualTo(new string('e', OutputRing.ViewLength)));
			Assert.That(full, Has.Length.EqualTo(OutputRing.DefaultCapacity));
			Assert.That(process.HasExited, Is.False);
		});
	}

	[Test]
	public async Task Shutdown_waits_for_a_start_in_flight_and_stops_it()
	{
		var script = await _store.CreateAsync("Late", "Persistent");
		using var entered = new SemaphoreSlim(0);
		using var proceed = new SemaphoreSlim(0);
		_launcher.OnStart = process =>
		{
			entered.Release();
			proceed.Wait();
			process.Ready();
		};

		var run = Task.Run(() => _manager.RunAsync(Managed(script.Id), CancellationToken.None));
		await entered.WaitAsync();
		var shutdown = _manager.ShutdownAsync();
		await Task.Delay(100);
		var finishedBeforeStart = shutdown.IsCompleted;
		proceed.Release();
		await shutdown;
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(finishedBeforeStart, Is.False);
			Assert.That(_launcher.Started.Single().HasExited, Is.True);
			Assert.That(_launcher.Started.Single().CloseRequests, Is.EqualTo(1));
			Assert.That(_launcher.Disposed, Is.True);
		});
	}

	[Test]
	public async Task Another_script_starts_while_one_is_restarting()
	{
		var slow = await _store.CreateAsync("Slow to stop", "Persistent");
		var other = await _store.CreateAsync("Other", "Persistent");
		await _manager.RunAsync(Managed(slow.Id), CancellationToken.None);
		_launcher.Started[0].HonoursClose = false;

		var restart = _manager.RunAsync(Managed(slow.Id) with { Policy = AutoHotkeyRunPolicy.Restart },
			CancellationToken.None);
		await WaitUntilAsync(() => _launcher.Started[0].CloseRequests == 1);
		var started = await _manager.RunAsync(Managed(other.Id), CancellationToken.None);
		var restartStillStopping = !restart.IsCompleted;
		var restarted = await restart;

		Assert.Multiple(() =>
		{
			Assert.That(started.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(restartStillStopping, Is.True);
			Assert.That(restarted.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(_launcher.Started, Has.Count.EqualTo(3));
		});
	}

	[Test]
	public async Task Without_the_helper_a_late_load_error_still_fails_the_run()
	{
		var script = await _store.CreateAsync("Slow broken", "x := (");
		await _store.UpdateSettingsAsync(script.Id, new AutoHotkeyScriptSettings(null, false, false, null));
		_launcher.OnStart = process => _ = Task.Run(async () =>
		{
			await Task.Delay(700);
			process.ErrorPipe.Write($"{process.Info.Arguments[^1]} (1) : ==> Missing \")\"\n");
			process.Exit(2);
		});

		var result = await _manager.RunAsync(Managed(script.Id), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(_manager.LatestRun(script.Id)?.Error?.Line, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Without_the_helper_the_ready_marker_ends_the_load_phase()
	{
		var script = await _store.CreateAsync("Plain", "Persistent");
		await _store.UpdateSettingsAsync(script.Id, new AutoHotkeyScriptSettings(null, false, false, null));
		var watch = Stopwatch.StartNew();

		var result = await _manager.RunAsync(Managed(script.Id), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
		});
	}

	[Test]
	public async Task A_reported_runtime_error_is_written_to_the_output_in_the_app_language()
	{
		var script = await _store.CreateAsync("Errors", "x := 1 / 0");
		_launcher.OnStart = process =>
		{
			process.Ready();
			process.Send("{\"t\":\"error\",\"message\":\"Divide by zero.\",\"file\":"
				+ JsonSerializer.Serialize(process.Info.Arguments[^1])
				+ ",\"line\":3}");
		};

		await _manager.RunAsync(Managed(script.Id), CancellationToken.None);
		var expected = AutoHotkeyText.Resolve(
			AppStrings.Integrations.AutoHotkey.Page.ErrorWithLine(3, "Divide by zero."));
		await WaitUntilAsync(() => _manager.LatestRun(script.Id)?.Output.Contains(expected) == true);

		Assert.That(_manager.LatestRun(script.Id)?.Output, Does.Contain(expected));
	}

	private static AutoHotkeyRunRequest Managed(Guid scriptId)
	{
		return new AutoHotkeyRunRequest { Kind = AutoHotkeyTargetKind.Managed, ScriptId = scriptId };
	}

	private static AutoHotkeyRunRequest Inline(string code, UserVariableContext context)
	{
		return new AutoHotkeyRunRequest { Kind = AutoHotkeyTargetKind.Inline, Code = code, VariableContext = context };
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(10);
		}
	}
}
