using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.AutoHotkey.Configuration;
using MacroDeckHost.Integrations.AutoHotkey.Native;
using MacroDeckHost.Integrations.AutoHotkey.Providers;
using MacroDeckHost.Integrations.AutoHotkey.State;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Windows.AutoHotkey;

[Platform("Win")]
[SupportedOSPlatform("windows")]
[NonParallelizable]
internal sealed class AutoHotkeyRealProcessTests
{
	private static readonly ILogger _logger = Logger.None;
	private static readonly TimeSpan _waitTimeout = TimeSpan.FromSeconds(30);

	private AutoHotkeyInstallation _installation = null!;
	private string _root = null!;
	private AutoHotkeyLauncher _launcher = null!;
	private AutoHotkeyScriptStore _store = null!;
	private MemoryUserVariables _variables = null!;
	private MemoryNotifier _notifier = null!;
	private AutoHotkeyProcessManager _manager = null!;

	[OneTimeSetUp]
	public async Task FindAutoHotkey()
	{
		_installation = await AutoHotkeyInstallation.DetectAsync(new AutoHotkeyMachine(), null, CancellationToken.None);
		if (!_installation.IsValid)
		{
			Assert.Ignore("AutoHotkey v2 is not installed on this machine.");
		}
	}

	[SetUp]
	public async Task SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "ahk-real-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		_launcher = new AutoHotkeyLauncher();
		_store = new AutoHotkeyScriptStore(new MemoryStorage(), TimeProvider.System, _logger);
		await _store.LoadAsync();
		_variables = new MemoryUserVariables();
		_notifier = new MemoryNotifier();
		_manager = new AutoHotkeyProcessManager(new AutoHotkeyRuntime
		{
			Launcher = _launcher,
			Folders = new AutoHotkeyRunFolders(Path.Combine(_root, "runs"), _logger),
			Store = _store,
			Installation = () => _installation,
			Settings = () => new AutoHotkeySettings(null, TimeSpan.FromMilliseconds(800)),
			Time = TimeProvider.System,
			Logger = _logger,
			UserVariables = _variables,
			Notifier = _notifier
		});
	}

	[TearDown]
	public async Task TearDown()
	{
		await _manager.DisposeAsync();
		_launcher.Dispose();
		_store.Dispose();
		try
		{
			Directory.Delete(_root, true);
		}
		catch (IOException)
		{
		}
	}

	[Test]
	public async Task Parameter_values_arrive_unchanged_and_cannot_alter_the_code()
	{
		var values = new[]
		{
			"say \"hi\"", "back`tick``", "line1\r\nline2\nline3", "tab\there", "{braces} %percent%",
			"a ;not a comment", "\"; ExitApp 7 ; \""
		};
		var parameters = values.Select((value, index) => (Key: $"p{index}", Value: value))
			.ToDictionary(pair => pair.Key, pair => pair.Value);
		var code = "out := \"\"\nfor key, value in MacroDeck.Params\n    out .= key \"=\" value \"|\"\n"
			+ "MacroDeck.SetResult(out)";

		var result = await RunInlineAsync(code, parameters);

		var expected = string.Concat(parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
			.Select(pair => $"{pair.Key}={pair.Value}|"));
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(AutoHotkeyRunStatus.Completed), result.ErrorMessage.ToString());
			Assert.That(result.Result, Is.EqualTo(expected));
		});
	}

	[Test]
	public async Task A_100_kib_parameter_value_arrives_whole()
	{
		var value = string.Concat(Enumerable.Repeat("ab\"c`d;e\r\n", 10 * 1024));
		var code = "v := MacroDeck.Params[\"big\"]\n"
			+ "MacroDeck.SetResult(StrLen(v) \"|\" StrReplace(SubStr(v, 1, 10), \"`r`n\", \"<>\") \"|\" "
			+ "StrReplace(SubStr(v, -10), \"`r`n\", \"<>\"))";

		var result = await RunInlineAsync(code, new Dictionary<string, string> { ["big"] = value });

		Assert.That(result.Result, Is.EqualTo($"{value.Length}|ab\"c`d;e<>|ab\"c`d;e<>"));
	}

	[Test]
	public async Task File_arguments_reach_a_args_unchanged()
	{
		var file = Path.Combine(_root, "args.ahk");
		await File.WriteAllTextAsync(file,
			"out := \"\"\nfor argument in A_Args\n    out .= \"[\" argument \"]\"\nMacroDeck.SetResult(out)\n");

		var result = await _manager.RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.File,
				FilePath = file,
				IncludeHelper = true,
				Arguments = ["with space", "quo\"te", @"trail\", "", @"C:\path with\"],
				Wait = true,
				Timeout = _waitTimeout
			},
			CancellationToken.None);

		Assert.That(result.Result, Is.EqualTo("[with space][quo\"te][trail\\][][C:\\path with\\]"));
	}

	[Test]
	public async Task Exit_code_and_output_are_reported_without_a_dialog()
	{
		var result = await RunInlineAsync("FileAppend(\"printed`n\", \"*\")\nExitApp(5)");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(result.ExitCode, Is.EqualTo(5));
			Assert.That(result.Output, Does.Contain("printed"));
		});
	}

	[Test]
	public async Task A_runtime_error_is_reported_instead_of_a_modal_dialog()
	{
		var result = await RunInlineAsync("x := 1\ny := x / 0");

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
	}

	[Test]
	public async Task Check_syntax_reports_the_line_of_a_load_error()
	{
		var valid = await _manager.CheckSyntaxAsync("x := 1", true, CancellationToken.None);
		var broken = await _manager.CheckSyntaxAsync("x := 1\ny := (\nz := 2", true, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(valid.IsValid, Is.True, valid.Message);
			Assert.That(broken.IsValid, Is.False);
			Assert.That(broken.Line, Is.EqualTo(2));
			Assert.That(broken.Message, Is.Not.Empty);
		});
	}

	[Test]
	public async Task A_load_error_fails_the_run_with_its_line()
	{
		var script = await _store.CreateAsync("Broken", "x := 1\ny := (");

		var result = await _manager.RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Managed, ScriptId = script.Id
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(_manager.LatestRun(script.Id)?.Error?.Line, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Stop_closes_the_script_so_its_exit_handler_runs()
	{
		var marker = Path.Combine(_root, "exit-reason.txt");
		var script = await _store.CreateAsync("Persistent",
			"Persistent\nOnExit((reason, *) => FileAppend(reason, MacroDeck.Params[\"marker\"]))");

		var started = await _manager.RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Managed,
				ScriptId = script.Id,
				Parameters = new Dictionary<string, string> { ["marker"] = marker }
			},
			CancellationToken.None);
		var stopped = await _manager.StopAsync(AutoHotkeyIdentity.Managed(script.Id), true);

		Assert.Multiple(() =>
		{
			Assert.That(started.Status, Is.EqualTo(AutoHotkeyRunStatus.Started));
			Assert.That(stopped, Is.EqualTo(1));
			Assert.That(File.ReadAllText(marker), Is.EqualTo("Close"));
			Assert.That(_manager.IsScriptRunning(script.Id), Is.False);
		});
	}

	[Test]
	public async Task A_script_that_refuses_to_close_is_ended_after_the_timeout()
	{
		var started = Stopwatch.StartNew();

		var result = await RunInlineAsync("Persistent\nOnExit((*) => 1)", timeout: TimeSpan.FromSeconds(1));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
			Assert.That(started.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
			Assert.That(_manager.RunningCount, Is.Zero);
		});
	}

	[Test]
	public async Task Closing_the_job_ends_the_script_but_a_program_it_launched_survives()
	{
		var child = Path.Combine(_root, "child.ahk");
		await File.WriteAllTextAsync(child, "Persistent\n");
		var code = "Run('\"' A_AhkPath '\" \"' MacroDeck.Params[\"child\"] '\"', , , &pid)\n"
			+ "MacroDeck.SetVar(\"child_pid\", pid)\nPersistent";
		await _manager.RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Inline,
				Code = code,
				Parameters = new Dictionary<string, string> { ["child"] = child }
			},
			CancellationToken.None);
		await WaitUntilAsync(() => _variables["child_pid"] is not null);
		var childProcess = Process.GetProcessById(int.Parse(_variables["child_pid"]!,
			NumberStyles.None,
			CultureInfo.InvariantCulture));

		try
		{
			_launcher.Dispose();
			await WaitUntilAsync(() => _manager.RunningCount == 0);

			Assert.Multiple(() =>
			{
				Assert.That(_manager.RunningCount, Is.Zero);
				Assert.That(childProcess.HasExited, Is.False);
			});
		}
		finally
		{
			childProcess.Kill();
			childProcess.Dispose();
		}
	}

	[Test]
	public async Task The_helper_sets_reads_notifies_and_logs()
	{
		_variables["input"] = "value";
		var code = "MacroDeck.SetVar(\"output\", MacroDeck.GetVar(\"input\") \"!\")\n"
			+ "MacroDeck.Notify(\"Title\", \"Message\", \"warning\")\n"
			+ "MacroDeck.Log(\"logged line\")\n"
			+ "MacroDeck.SetResult(MacroDeck.GetVar(\"missing\", \"fallback\"))";

		var result = await RunInlineAsync(code);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(AutoHotkeyRunStatus.Completed), result.ErrorMessage.ToString());
			Assert.That(_variables["output"], Is.EqualTo("value!"));
			Assert.That(_notifier.Notifications.Single().Level, Is.EqualTo(UserNotificationLevel.Warning));
			Assert.That(result.Output, Does.Contain("logged line"));
			Assert.That(result.Result, Is.EqualTo("fallback"));
		});
	}

	[Test]
	public async Task A_message_flood_does_not_stall_the_script_or_its_variable_reads()
	{
		_variables["input"] = "value";
		var code = "Loop 3000\n    MacroDeck.Log(\"line \" A_Index)\n"
			+ "MacroDeck.SetResult(\"done \" MacroDeck.GetVar(\"input\", \"fallback\"))";

		var result = await RunInlineAsync(code);

		Assert.That(result.Result, Is.EqualTo("done value").Or.EqualTo("done fallback"));
	}

	[Test]
	public async Task Without_the_helper_a_load_error_is_reported_with_its_line_when_not_waiting()
	{
		var script = await _store.CreateAsync("Broken without helper", "x := 1\ny := (");
		await _store.UpdateSettingsAsync(script.Id, new AutoHotkeyScriptSettings(null, false, false, null));

		var result = await _manager.RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Managed, ScriptId = script.Id
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo("EXIT_CODE"));
			Assert.That(_manager.LatestRun(script.Id)?.Error?.Line, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Without_the_helper_a_loaded_script_starts_and_reports_runtime_errors()
	{
		var script = await _store.CreateAsync("Plain", "Sleep 300\nx := 1 / 0");
		await _store.UpdateSettingsAsync(script.Id, new AutoHotkeyScriptSettings(null, false, false, null));

		var result = await _manager.RunAsync(new AutoHotkeyRunRequest
			{
				Kind = AutoHotkeyTargetKind.Managed, ScriptId = script.Id, Wait = true, Timeout = _waitTimeout
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			Assert.That(_manager.LatestRun(script.Id)?.Error?.Line, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_disposed_launcher_starts_no_process()
	{
		using var launcher = new AutoHotkeyLauncher();
		launcher.Dispose();

		Assert.Throws<ObjectDisposedException>(() => launcher.Start(new AutoHotkeyStartInfo(
			_installation.ExecutablePath!,
			["/ErrorStdOut=UTF-8", Path.Combine(_root, "never.ahk")],
			_root)));
	}

	[Test]
	public void Only_run_folders_whose_lock_is_free_are_cleaned_up()
	{
		var folders = new AutoHotkeyRunFolders(Path.Combine(_root, "cleanup"), _logger);
		using var held = folders.Create(Guid.NewGuid());
		var stale = Path.Combine(folders.Root, Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(stale);
		File.WriteAllText(Path.Combine(stale, ".lock"), string.Empty);

		var deleted = folders.DeleteStale();

		Assert.Multiple(() =>
		{
			Assert.That(deleted, Is.EqualTo(1));
			Assert.That(Directory.Exists(stale), Is.False);
			Assert.That(Directory.Exists(held.FullPath), Is.True);
		});
	}

	private async Task<AutoHotkeyRunResult> RunInlineAsync(
		string code,
		IReadOnlyDictionary<string, string>? parameters = null,
		TimeSpan? timeout = null)
	{
		return await _manager.RunAsync(new AutoHotkeyRunRequest
				{
					Kind = AutoHotkeyTargetKind.Inline,
					Code = code,
					Parameters = parameters ?? new Dictionary<string, string>(),
					Wait = true,
					Timeout = timeout ?? _waitTimeout,
					VariableContext = new UserVariableContext("widget")
				},
				CancellationToken.None)
			.ConfigureAwait(false);
	}

	private static async Task WaitUntilAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(15);
		while (!condition() && DateTime.UtcNow < deadline)
		{
			await Task.Delay(50);
		}
	}
}
