using MacroDeckHost.Integrations.Delegation;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

[TestFixture]
internal sealed class RunRemoteScriptActionDefinitionTests
{
	private static readonly string[] _singleScript = ["script-1"];

	private FakeDelegateClient _client = null!;
	private FakeTimeProvider _time = null!;
	private RecordingIntegrationConfig _config = null!;
	private DelegateRemoteManager _manager = null!;
	private RunRemoteScriptActionDefinition _action = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeDelegateClient();
		_time = new FakeTimeProvider();
		_config = new RecordingIntegrationConfig();
		_manager = new DelegateRemoteManager(() => _client, _time, new LoggerConfiguration().CreateLogger());
		_action = new RunRemoteScriptActionDefinition(() => _manager);
	}

	[TearDown]
	public void TearDown()
	{
		_manager.Dispose();
		_client.Dispose();
	}

	[Test]
	public async Task A_successful_run_posts_exactly_one_request_for_the_selected_script()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");

		var result = await RunAsync("script-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_client.CallCount("run", baseUrl), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_remote_run_that_reports_a_failed_execution_fails_the_action()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(false, "boom", "Failed");

		var result = await RunAsync("script-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task A_script_that_no_longer_exists_on_the_remote_fails_with_not_found()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunException = new DelegateNotFoundException();

		var result = await RunAsync("gone");

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task A_remote_that_is_switched_off_fails_with_not_connected_and_makes_no_login_attempt()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Reachable = false;
		AddEntry(baseUrl, "PC-A", "inst-1");
		await ReloadAndStart();

		var result = await RunAsync("script-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(_client.LoginCount, Is.Zero);
		});
	}

	[Test]
	public async Task A_run_that_exceeds_the_timeout_fails_at_the_configured_bound()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).HangRun = true;

		var defaultRunTask = RunAsync("script-1");
		_time.Advance(TimeSpan.FromSeconds(59));
		Assert.That(defaultRunTask.IsCompleted, Is.False, "must not fail before the default 60s bound");
		_time.Advance(TimeSpan.FromSeconds(2));
		var defaultResult = await defaultRunTask;
		Assert.That(defaultResult.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));

		var shortRunTask = RunAsync("script-1", timeoutMilliseconds: 5_000);
		_time.Advance(TimeSpan.FromSeconds(4));
		Assert.That(shortRunTask.IsCompleted, Is.False, "must not fail before the shorter, user-set bound");
		_time.Advance(TimeSpan.FromSeconds(2));
		var shortResult = await shortRunTask;
		Assert.That(shortResult.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
	}

	[Test]
	public async Task An_instance_that_no_longer_exists_never_runs_on_a_different_remote()
	{
		var otherBaseUrl = await AddReachableRemote("inst-a", "PC-A");

		var result = await RunAsync("script-1", instanceId: "inst-does-not-exist");

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(_client.CallCount("run", otherBaseUrl), Is.Zero);
		});
	}

	[TestCase(0, 1)]
	[TestCase(3, 4)]
	public async Task Delegating_sends_a_hop_count_one_higher_than_the_current_call_depth(
		int callDepth,
		int expectedHopCount)
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");

		await RunAsync("script-1", callDepth: callDepth);

		Assert.That(_client.For(baseUrl).RunCallDepths, Is.EqualTo(new[] { expectedHopCount }));
	}

	[Test]
	public async Task A_remote_that_reports_the_depth_budget_exhausted_fails_with_provider_rejected()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunException = new DelegateDepthExceededException();

		var result = await RunAsync("script-1");

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
	}

	[Test]
	public async Task Supplied_inputs_reach_the_remote_run_call()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(true, null, "Succeeded", ["scene", "volume"]);

		var result = await RunAsync("script-1", inputs: [("scene", "Intermission"), ("volume", 70d)]);

		var sent = _client.For(baseUrl).RunInputs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(sent!["scene"], Is.EqualTo("Intermission"));
			Assert.That(sent["volume"], Is.EqualTo(70d));
		});
	}

	[Test]
	public async Task A_remote_that_does_not_report_the_inputs_as_applied_fails_the_action()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(true, null, "Succeeded");

		var result = await RunAsync("script-1", inputs: [("scene", "Intermission")]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("PC-A").And.Contain("Script One"));
		});
	}

	[Test]
	public async Task A_run_with_no_inputs_against_that_same_older_remote_still_succeeds()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(true, null, "Succeeded");

		var result = await RunAsync("script-1");

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	// Acceptance scenario S2.4: the delegating side must fail loudly whenever a supplied name comes back
	// unapplied, and must not fail merely because the remote reported extra names (a superset).
	[Test]
	public async Task A_remote_dropping_one_of_two_supplied_inputs_fails_and_names_the_dropped_one()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(true, null, "Succeeded", ["scene"]);

		var result = await RunAsync("script-1", inputs: [("scene", "Intermission"), ("volume", 70d)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Does.Contain("PC-A").And.Contain("Script One").And.Contain("volume"));
		});
	}

	[Test]
	public async Task A_remote_reporting_no_applied_inputs_fails()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(true, null, "Succeeded", []);

		var result = await RunAsync("script-1", inputs: [("scene", "Intermission"), ("volume", 70d)]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
		});
	}

	[Test]
	public async Task A_remote_reporting_a_superset_of_the_supplied_inputs_still_succeeds()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A");
		_client.For(baseUrl).RunHandler =
			_ => new DelegateRunResult(true, null, "Succeeded", ["scene", "volume", "muted"]);

		var result = await RunAsync("script-1", inputs: [("scene", "Intermission"), ("volume", 70d)]);

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	// Acceptance scenario 8 (host half): a remote script that runs on a widget is not supported over
	// delegation - the outbound call carries no owner widget of any kind, not even the block's own
	// invoking widget, because IDelegateClient.RunScriptAsync has no such parameter to begin with.
	[Test]
	public async Task No_owner_widget_is_sent_even_when_the_script_runs_on_a_widget()
	{
		var baseUrl = await AddReachableRemote("inst-1", "PC-A", runsOnWidget: true);
		_client.For(baseUrl).RunHandler = _ => new DelegateRunResult(true, null, "Succeeded", ["scene"]);

		var result = await RunAsync("script-1", ownerWidgetId: "w1", inputs: [("scene", "Intermission")]);

		var sent = _client.For(baseUrl).RunInputs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(sent, Has.Count.EqualTo(1), "the outbound payload must carry only the explicit inputs");
			Assert.That(sent!["scene"], Is.EqualTo("Intermission"));
		});
	}

	// Issue #806: a blank instance with two or more configured remotes must not answer with a
	// permanently empty, unexplained script picker - it must say which instance to pick.
	[Test]
	public async Task Two_configured_remotes_with_no_instance_chosen_yield_no_scripts_and_the_ambiguous_message()
	{
		await AddReachableRemote("inst-1", "PC-A");
		await AddReachableRemote("inst-2", "PC-B");

		var result = await GetScriptOptionsAsync(instanceId: null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(TestLocalization.Resolve(result.Error),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Delegation.Errors.Ambiguous())));
		});
	}

	// The counterexample that stops "always error when instance is blank": with exactly one remote
	// configured, a blank instance resolves unambiguously and must behave exactly as before.
	[Test]
	public async Task A_single_configured_remote_with_no_instance_chosen_still_yields_its_scripts_and_no_error()
	{
		await AddReachableRemote("inst-1", "PC-A");

		var result = await GetScriptOptionsAsync(instanceId: null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options.Select(o => o.Value), Is.EqualTo(_singleScript));
			Assert.That(result.Error.IsEmpty, Is.True);
		});
	}

	[Test]
	public async Task An_unknown_instance_id_yields_no_scripts_and_the_not_configured_message()
	{
		await AddReachableRemote("inst-1", "PC-A");

		var result = await GetScriptOptionsAsync(instanceId: "inst-does-not-exist");

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(TestLocalization.Resolve(result.Error),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Delegation.Errors.InstanceNotFound())));
		});
	}

	[Test]
	public async Task Nothing_configured_at_all_yields_no_scripts_and_the_none_configured_message()
	{
		var result = await GetScriptOptionsAsync(instanceId: null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(TestLocalization.Resolve(result.Error),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Delegation.Errors.NoneConfigured())));
		});
	}

	[Test]
	public async Task A_resolvable_instance_with_no_scripts_yields_an_empty_list_and_no_error()
	{
		var baseUrl = new Uri("http://pc-a.local:5000");
		AddEntry(baseUrl, "PC-A", "inst-1");
		await ReloadAndStart();

		var result = await GetScriptOptionsAsync(instanceId: "inst-1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(result.Error.IsEmpty, Is.True);
		});
	}

	[Test]
	public async Task Nothing_configured_at_all_leaves_the_instance_picker_empty_with_the_none_configured_message()
	{
		var result = await _action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "instance",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(TestLocalization.Resolve(result.Error),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Delegation.Errors.NoneConfigured())));
		});
	}

	private async Task<DynamicOptionsResult> GetScriptOptionsAsync(string? instanceId)
	{
		var currentParameters = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (instanceId is not null)
		{
			currentParameters["instance"] = instanceId;
		}

		return await _action.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "scriptId",
				CurrentParameters = currentParameters
			},
			CancellationToken.None);
	}

	private async Task<Uri> AddReachableRemote(string instanceId, string machineName, bool runsOnWidget = false)
	{
		var baseUrl = new Uri($"http://{machineName.ToLowerInvariant()}.local:5000");
		_client.For(baseUrl).Scripts["script-1"] = "Script One";
		_client.For(baseUrl).ScriptRunsOnWidget["script-1"] = runsOnWidget;
		AddEntry(baseUrl, machineName, instanceId);
		await ReloadAndStart();
		return baseUrl;
	}

	private Guid AddEntry(Uri baseUrl, string machineName, string instanceId)
		=> _config.AddEntry(machineName,
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[DelegateConfigKeys.BaseUrl] = baseUrl.ToString(),
				[DelegateConfigKeys.Username] = "admin",
				[DelegateConfigKeys.InstanceId] = instanceId,
				[DelegateConfigKeys.MachineName] = machineName,
				[DelegateConfigKeys.InstanceKey] = DelegateSlug.For(machineName, instanceId),
				[DelegateConfigKeys.ConfiguredAt] = _time.Now.ToString("o")
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
				{ [DelegateConfigKeys.Password] = "correct-password" });

	private async Task ReloadAndStart()
	{
		await _manager.ReloadAsync(_config);
		_manager.StartAll();
	}

	private async Task<ActionResult> RunAsync(
		string scriptId,
		string? instanceId = null,
		int callDepth = 0,
		double? timeoutMilliseconds = null,
		(string Name, object Value)[]? inputs = null,
		string? ownerWidgetId = null)
	{
		var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["scriptId"] = scriptId };
		foreach (var (name, value) in inputs ?? [])
		{
			parameters["input:" + name] = value;
		}

		if (instanceId is not null)
		{
			parameters["instance"] = instanceId;
		}

		if (timeoutMilliseconds is not null)
		{
			parameters["timeout"] = timeoutMilliseconds.Value;
		}

		return await _action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = parameters,
			OwnerWidgetId = ownerWidgetId,
			CancellationToken = CancellationToken.None,
			CallDepth = callDepth
		});
	}
}
