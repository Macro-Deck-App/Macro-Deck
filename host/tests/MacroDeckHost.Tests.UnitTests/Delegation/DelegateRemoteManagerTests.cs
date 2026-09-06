using System.Text.Json;
using MacroDeckHost.Integrations.Delegation;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

[TestFixture]
internal sealed class DelegateRemoteManagerTests
{
	private FakeDelegateClient _client = null!;
	private FakeTimeProvider _time = null!;
	private RecordingIntegrationConfig _config = null!;
	private DelegateRemoteManager _manager = null!;
	private DelegateIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeDelegateClient();
		_time = new FakeTimeProvider();
		_config = new RecordingIntegrationConfig();
		_manager = new DelegateRemoteManager(() => _client, _time, new LoggerConfiguration().CreateLogger());
		_integration = new DelegateIntegration(_manager);
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
		_manager.Dispose();
		_client.Dispose();
	}

	[Test]
	public async Task A_rejected_credential_stops_further_login_attempts_until_the_remote_is_reconfigured()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Password = "the-real-password";
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", password: "wrong-password");
		await ReloadAndStart();

		var codes = new List<string>();
		for (var i = 0; i < 5; i++)
		{
			var result = await RunAction("script-1");
			codes.Add(result.ErrorCode!);

			_time.Advance(TimeSpan.FromMinutes(10));
		}

		Assert.Multiple(() =>
		{
			Assert.That(codes, Has.All.EqualTo(codes[0]));
			Assert.That(codes[0], Is.Not.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(_client.LoginCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_unauthorized_data_call_whose_renewed_sign_in_also_fails_is_reported_as_permission_denied()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Password = "the-real-password";
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", password: "the-real-password");
		await ReloadAndStart();
		Assert.That(_client.LoginCount, Is.EqualTo(1), "the initial, successful login must already have happened");

		_client.For(baseUrl).RunException = new DelegateUnauthorizedException();
		_client.For(baseUrl).Password = "rotated-elsewhere";

		ActionResult result = default!;
		Assert.DoesNotThrowAsync(async () => result = await RunAction("script-1"));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
		});
	}

	[Test]
	public async Task Reconfiguring_with_a_correct_password_clears_the_stop()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Scripts["script-1"] = "Script One";
		_client.For(baseUrl).Password = "the-real-password";
		var entryId = AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", password: "wrong-password");
		await ReloadAndStart();
		await RunAction("script-1");
		Assert.That(_client.LoginCount, Is.EqualTo(1), "the wrong password must have been spent already");

		_config.RemoveEntry(entryId);
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", password: "the-real-password");
		await ReloadAndStart();

		var result = await RunAction("script-1");

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task A_429_is_honoured_for_the_advertised_retry_after_before_any_further_login()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).LoginException = new DelegateRateLimitedException(TimeSpan.FromMinutes(10));
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1");
		await ReloadAndStart();
		Assert.That(_client.LoginCount, Is.EqualTo(1), "the first login (which 429s) must already have happened");

		_time.Advance(TimeSpan.FromMinutes(5));
		await RunAction("script-1");
		Assert.That(_client.LoginCount, Is.EqualTo(1), "still inside the throttle window");

		_time.Advance(TimeSpan.FromMinutes(6));
		await RunAction("script-1");

		Assert.That(_client.LoginCount, Is.EqualTo(2), "exactly one more login once the window elapsed");
	}

	[Test]
	public async Task The_instance_options_list_every_configured_remote_and_issue_no_network_requests()
	{
		var baseUrlA = new Uri("http://10.0.0.5:5000");
		var baseUrlB = new Uri("http://10.0.0.6:5000");
		_client.For(baseUrlA).Reachable = false;
		_client.For(baseUrlB).Reachable = false;
		AddEntry("A", baseUrlA, "PC-A", "inst-a");
		AddEntry("B", baseUrlB, "PC-B", "inst-b");
		await _manager.ReloadAsync(_config);

		var options = _manager.InstanceOptions();

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(o => o.Value), Is.EquivalentTo(new List<string> { "inst-a", "inst-b" }));
			Assert.That(_client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task The_script_options_answer_from_the_last_known_list_while_the_remote_is_offline()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Reachable = false;
		var scripts = new Dictionary<string, string>(StringComparer.Ordinal) { ["s1"] = "Alpha", ["s2"] = "Beta" };
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", scripts: scripts);
		await _manager.ReloadAsync(_config);

		var options = _manager.Resolve("inst-1").Remote!.ScriptOptions();

		Assert.Multiple(() =>
		{
			Assert.That(options.Select(o => o.Value), Is.EquivalentTo(new List<string> { "s1", "s2" }));
			Assert.That(_client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task The_script_options_are_scoped_to_the_selected_instance()
	{
		var baseUrlA = new Uri("http://10.0.0.5:5000");
		var baseUrlB = new Uri("http://10.0.0.6:5000");
		AddEntry("A",
			baseUrlA,
			"PC-A",
			"inst-a",
			scripts: new Dictionary<string, string>(StringComparer.Ordinal) { ["a1"] = "alpha" });
		AddEntry("B",
			baseUrlB,
			"PC-B",
			"inst-b",
			scripts: new Dictionary<string, string>(StringComparer.Ordinal) { ["b1"] = "beta" });
		await _manager.ReloadAsync(_config);

		var options = _manager.Resolve("inst-b").Remote!.ScriptOptions();

		Assert.That(options.Select(o => TestLocalization.Resolve(o.Label)), Is.EqualTo(new List<string?> { "beta" }));
	}

	[TestCase("password")]
	[TestCase("address")]
	[TestCase("machine-name")]
	public async Task A_bound_instance_keeps_working_after_the_remote_is_reconfigured(string what)
	{
		var originalUrl = new Uri("http://10.0.0.5:5000");
		_client.For(originalUrl).Scripts["s1"] = "Alpha";
		_client.For(originalUrl).Password = "old-password";
		var entryId = AddEntry("Remote", originalUrl, "GAMING-PC", "inst-1", password: "old-password");
		await ReloadAndStart();
		var boundInstanceId = _manager.Resolve(null).Remote!.Instance.InstanceId;

		_config.RemoveEntry(entryId);

		var newUrl = what == "address" ? new Uri("http://10.0.0.9:5000") : originalUrl;
		var newMachineName = what == "machine-name" ? "GAMING-PC-2" : "GAMING-PC";
		var newPassword = what == "password" ? "new-password" : "old-password";
		_client.For(newUrl).Password = newPassword;
		_client.For(newUrl).Scripts["s1"] = "Alpha";
		AddEntry("Remote", newUrl, newMachineName, boundInstanceId, password: newPassword);
		await ReloadAndStart();

		var resolved = _manager.Resolve(boundInstanceId);
		var result = await RunAction("s1", boundInstanceId);

		Assert.Multiple(() =>
		{
			Assert.That(resolved.Kind, Is.EqualTo(DelegateResolutionKind.Found));
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_client.CallCount("run", newUrl), Is.EqualTo(1));
			Assert.That(_manager.InstanceOptions(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_rejected_credential_is_an_error_issue_that_offers_reconfiguration()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", password: "wrong-password");
		await ReloadAndStart();
		await RunAction("s1"); // spends the one login attempt, which 401s

		var issues = _manager.Issues();

		Assert.Multiple(() =>
		{
			Assert.That(issues, Has.Count.EqualTo(1));
			Assert.That(issues[0].Severity, Is.EqualTo(IntegrationIssueSeverity.Error));
			Assert.That(TestLocalization.Resolve(issues[0].ActionLabel), Is.Not.Null);
			Assert.That(TestLocalization.Resolve(issues[0].Title), Does.Contain("GAMING-PC"));
		});

		var resolution = await ResolveIssue(issues[0].Id);
		Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
	}

	[Test]
	public async Task A_remote_that_is_merely_switched_off_reports_no_issue_until_the_grace_period_elapses()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Reachable = false;
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1");
		await ReloadAndStart();

		_time.Advance(TimeSpan.FromMinutes(4));
		Assert.That(_manager.Issues(), Is.Empty, "still inside the grace period");

		_time.Advance(TimeSpan.FromMinutes(2));
		var firstCheck = _manager.Issues();

		_time.Advance(TimeSpan.FromMinutes(10));
		var secondCheck = _manager.Issues();

		Assert.Multiple(() =>
		{
			Assert.That(firstCheck, Has.Count.EqualTo(1));
			Assert.That(firstCheck[0].Severity, Is.LessThan(IntegrationIssueSeverity.Error));
			Assert.That(secondCheck, Has.Count.EqualTo(1), "still exactly one issue after further failed probes");
		});
	}

	[Test]
	public async Task An_unreachable_remote_that_comes_back_clears_its_issue_without_user_action()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Reachable = false;
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1");
		await ReloadAndStart();
		_time.Advance(TimeSpan.FromMinutes(10));
		Assert.That(_manager.Issues(), Is.Not.Empty);

		_client.For(baseUrl).Reachable = true;
		_time.Advance(TimeSpan.FromMinutes(10));

		Assert.That(_manager.Issues(), Is.Empty);
	}

	[Test]
	public async Task GetIssuesAsync_issues_no_network_requests()
	{
		var baseUrl = new Uri("http://10.0.0.5:5000");
		_client.For(baseUrl).Reachable = false;
		AddEntry("Remote", baseUrl, "GAMING-PC", "inst-1", password: "wrong-password");
		await ReloadAndStart();
		_time.Advance(TimeSpan.FromMinutes(10));

		var before = _client.Calls.Count;
		_manager.Issues();
		var after = _client.Calls.Count;

		Assert.That(after, Is.EqualTo(before));
	}

	private Guid AddEntry(
		string title,
		Uri baseUrl,
		string machineName,
		string instanceId,
		string username = "admin",
		string password = "correct-password",
		DateTimeOffset? configuredAt = null,
		IReadOnlyDictionary<string, string>? scripts = null)
	{
		var instanceKey = DelegateSlug.For(machineName, instanceId);
		return _config.AddEntry(title,
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[DelegateConfigKeys.BaseUrl] = baseUrl.ToString(),
				[DelegateConfigKeys.Username] = username,
				[DelegateConfigKeys.InstanceId] = instanceId,
				[DelegateConfigKeys.MachineName] = machineName,
				[DelegateConfigKeys.InstanceKey] = instanceKey,
				[DelegateConfigKeys.ConfiguredAt] = (configuredAt ?? _time.Now).ToString("o"),
				[DelegateConfigKeys.RemoteScripts] = scripts is null ? null : JsonSerializer.Serialize(scripts)
			},
			new Dictionary<string, string>(StringComparer.Ordinal) { [DelegateConfigKeys.Password] = password });
	}

	private async Task ReloadAndStart()
	{
		await _manager.ReloadAsync(_config);
		_manager.StartAll();
	}

	private async Task<ActionResult> RunAction(string scriptId, string? instanceId = null)
	{
		var action = new RunRemoteScriptActionDefinition(() => _manager);
		var executor = action.CreateExecutor();

		var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["scriptId"] = scriptId };
		if (instanceId is not null)
		{
			parameters["instance"] = instanceId;
		}

		return await executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = parameters,
			CancellationToken = CancellationToken.None
		});
	}

	private Task<IssueResolution> ResolveIssue(string issueId) => _integration.ResolveIssueAsync(issueId);
}
