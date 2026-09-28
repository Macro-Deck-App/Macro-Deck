using System.Globalization;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations.Delegation;
using MacroDeckHost.Integrations.Delegation.Protocol;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

[TestFixture]
internal sealed class DelegateSharedVariableImportTests
{
	private static readonly Uri GamingPc = new("http://10.0.0.5:8193");
	private static readonly Uri StreamPc = new("http://10.0.0.6:8193");

	private FakeDelegateClient _client = null!;
	private FakeTimeProvider _time = null!;
	private RecordingIntegrationConfig _config = null!;
	private DelegateRemoteManager _manager = null!;
	private DelegateIntegration _integration = null!;
	private RecordingVariableApi _variables = null!;
	private RecordingInvalidation _invalidation = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeDelegateClient();
		_time = new FakeTimeProvider();
		_config = new RecordingIntegrationConfig();
		_manager = new DelegateRemoteManager(() => _client, _time, new LoggerConfiguration().CreateLogger());
		_integration = new DelegateIntegration(_manager);
		_variables = new RecordingVariableApi();
		_invalidation = new RecordingInvalidation();
		_integration.UseVariablePollingInvalidation(_invalidation);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _integration.ShutdownAsync();
		_integration.Dispose();
		_manager.Dispose();
		_client.Dispose();
	}

	[Test]
	public async Task Without_the_import_option_only_the_connection_variable_exists_and_nothing_is_fetched()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: false);

		await InitializeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Names(), Is.EqualTo(new[] { "delegate_gaming_pc_connected" }));
			Assert.That(_client.CallCount("shared-variables", GamingPc), Is.Zero);
		});
	}

	[Test]
	public async Task A_shared_variable_appears_under_the_remote_prefix_with_the_remote_value()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);

		await InitializeAsync();
		await RefreshAsync();

		var definition = Declared("gaming_pc_obs_streaming");
		var reading = await _integration.ReadAsync(definition.ResolvedId!);

		Assert.Multiple(() =>
		{
			Assert.That(definition.Type, Is.EqualTo(VariableType.Boolean));
			Assert.That(reading.Value, Is.EqualTo(true));
			Assert.That(_invalidation.Stale, Does.Contain(DelegateIntegration.IntegrationId));
		});
	}

	[Test]
	public async Task A_remote_that_cannot_be_reached_makes_its_variables_unavailable_without_an_error()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		var id = Declared("gaming_pc_obs_streaming").ResolvedId!;

		_client.For(GamingPc).SharedVariablesException = new DelegateUnreachableException();
		await RefreshAsync();

		var reading = await _integration.ReadAsync(id);

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.Null);
			Assert.That(Names(), Does.Contain("gaming_pc_obs_streaming"),
				"a temporary outage must not remove the variable");
		});
	}

	[Test]
	public async Task Two_remotes_sharing_the_same_name_give_two_separate_variables()
	{
		Share(GamingPc, Numeric("fps", "60"));
		Share(StreamPc, Numeric("fps", "30"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		AddEntry(StreamPc, "STREAM-PC", "inst-2", importShared: true);

		await InitializeAsync();
		await RefreshAsync();

		var gaming = await _integration.ReadAsync(Declared("gaming_pc_fps").ResolvedId!);
		var stream = await _integration.ReadAsync(Declared("stream_pc_fps").ResolvedId!);

		Assert.Multiple(() =>
		{
			Assert.That(gaming.Value, Is.EqualTo(60m));
			Assert.That(stream.Value, Is.EqualTo(30m));
		});
	}

	[Test]
	public async Task A_variable_the_remote_stops_sharing_is_removed_without_a_restart()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"), Text("scene", "Main"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		await RegisterDeclaredAsync();

		_client.For(GamingPc).SharedVariables.RemoveAll(v => v.Name == "scene");
		_invalidation.Stale.Clear();
		await RefreshAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Names(), Does.Not.Contain("gaming_pc_scene"));
			Assert.That(OwnedNames(), Does.Not.Contain("gaming_pc_scene"));
			Assert.That(OwnedNames(), Does.Contain("gaming_pc_obs_streaming"));
			Assert.That(_invalidation.Stale, Does.Contain(DelegateIntegration.IntegrationId));
		});
	}

	[Test]
	public async Task A_shared_variable_the_remote_has_not_registered_yet_keeps_its_import()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		await RegisterDeclaredAsync();

		_client.For(GamingPc).SharedVariables[0] = Boolean("obs_streaming", string.Empty) with
		{
			Present = false,
			Available = false
		};
		await RefreshAsync();

		var reading = await _integration.ReadAsync(Declared("gaming_pc_obs_streaming").ResolvedId!);

		Assert.Multiple(() =>
		{
			Assert.That(OwnedNames(), Does.Contain("gaming_pc_obs_streaming"));
			Assert.That(reading.Value, Is.Null);
		});
	}

	[Test]
	public async Task Turning_the_import_off_removes_the_imported_variables_on_reinitialize()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		var entryId = AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		await RegisterDeclaredAsync();

		_config.Strings[(entryId, DelegateConfigKeys.ImportSharedVariables)] = "false";
		await _integration.ShutdownAsync();
		await InitializeAsync();

		Assert.Multiple(() =>
		{
			Assert.That(OwnedNames(), Does.Not.Contain("gaming_pc_obs_streaming"));
			Assert.That(OwnedNames(), Does.Contain("delegate_gaming_pc_connected"),
				"the connection variable is not an import and stays");
		});
	}

	[Test]
	public async Task Imports_survive_a_restart_while_the_remote_is_offline()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();

		await _integration.ShutdownAsync();
		_client.For(GamingPc).Reachable = false;
		_client.For(GamingPc).SharedVariablesException = new DelegateUnreachableException();
		await InitializeAsync();

		var reading = await _integration.ReadAsync(Declared("gaming_pc_obs_streaming").ResolvedId!);

		Assert.That(reading.Value, Is.Null);
	}

	[Test]
	public async Task A_remote_from_before_sharing_is_asked_again_only_after_a_long_pause()
	{
		_client.For(GamingPc).SharedVariablesException = new DelegateSharingUnsupportedException();
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await WaitFor(() => _client.CallCount("shared-variables", GamingPc) == 1);

		for (var i = 0; i < 10; i++)
		{
			_time.Advance(DelegateRemote.SharedVariableInterval);
			await Task.Delay(10);
		}

		var afterShortWait = _client.CallCount("shared-variables", GamingPc);
		_time.Advance(DelegateRemote.SharingUnsupportedRecheck);
		await WaitFor(() => _client.CallCount("shared-variables", GamingPc) > 1);

		var issues = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(afterShortWait, Is.EqualTo(1));
			Assert.That(Names(), Is.EqualTo(new[] { "delegate_gaming_pc_connected" }));
			Assert.That(issues, Is.Empty);
		});
	}

	[Test]
	public async Task A_remote_that_answers_with_an_error_is_retried_on_the_normal_cadence()
	{
		_client.For(GamingPc).SharedVariablesException = new DelegateServerErrorException();
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await WaitFor(() => _client.CallCount("shared-variables", GamingPc) == 1);

		_time.Advance(DelegateRemote.SharedVariableInterval);

		await WaitFor(() => _client.CallCount("shared-variables", GamingPc) > 1);
		Assert.Pass();
	}

	[Test]
	public async Task A_rejected_session_is_renewed_once_for_the_shared_variable_list()
	{
		Share(GamingPc, Boolean("obs_streaming", "true"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		_client.For(GamingPc).SharedVariablesExceptions.Enqueue(new DelegateUnauthorizedException());

		await RefreshAsync();
		var reading = await _integration.ReadAsync(Declared("gaming_pc_obs_streaming").ResolvedId!);

		Assert.That(reading.Value, Is.EqualTo(true));
	}

	[Test]
	public async Task Writing_an_imported_variable_sends_the_value_to_the_remote()
	{
		Share(GamingPc, Numeric("volume", "10") with { CanWrite = true });
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		_client.For(GamingPc).SharedWriteExceptions.Enqueue(new DelegateUnauthorizedException());

		var result = await _integration.SetValueAsync(Declared("gaming_pc_volume").ResolvedId!, 12.5m);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(_client.For(GamingPc).SharedWrites.Last(), Is.EqualTo(("volume", "12.5")));
			Assert.That(Declared("gaming_pc_volume").Write, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_locked_remote_refusing_a_write_is_a_failed_write_with_its_own_message()
	{
		Share(GamingPc, Boolean("recording", "false") with { CanWrite = true });
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		_client.For(GamingPc).SharedWriteResult = new DelegateWriteResult(false, "HOST_LOCKED");

		var result = await _integration.SetValueAsync(Declared("gaming_pc_recording").ResolvedId!, true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.Not.EqualTo(VariableWriteStatus.Applied));
			Assert.That(TestLocalization.Resolve(result.Message), Does.Contain("GAMING-PC").And.Contain("locked"));
		});
	}

	[Test]
	public async Task A_read_only_remote_variable_is_declared_without_a_write_capability()
	{
		Share(GamingPc, Numeric("cpu", "12"));
		AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();

		Assert.That(Declared("gaming_pc_cpu").Write, Is.Null);
	}

	[Test]
	public async Task A_machine_name_starting_with_a_digit_still_gives_a_valid_name()
	{
		Share(GamingPc, Text("scene", "Main"));
		AddEntry(GamingPc, "2ndPC", "inst-1", importShared: true);
		await InitializeAsync();
		await RefreshAsync();

		var definition = Declared("v_2ndpc_scene");

		Assert.That(definition.ResolvedId, Is.Not.Null);
	}

	[Test]
	public async Task Colliding_names_from_two_remotes_keep_the_first_and_report_the_other()
	{
		Share(GamingPc, Text("main_x", "a"));
		Share(StreamPc, Text("x", "b"));
		AddEntry(GamingPc, "PC", "inst-1", importShared: true);
		AddEntry(StreamPc, "PC Main", "inst-2", importShared: true);
		await InitializeAsync();
		await RefreshAsync();
		await RegisterDeclaredAsync();

		var reading = await _integration.ReadAsync(Declared("pc_main_x").ResolvedId!);
		var issues = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(reading.Value, Is.EqualTo("a"));
			Assert.That(issues.Select(i => i.Id),
				Is.EqualTo(new[] { DelegateIntegration.SharedImportIssuePrefix + "inst-2" }));
			Assert.That(TestLocalization.Resolve(issues[0].Description), Does.Contain("x"));
		});
	}

	[Test]
	public async Task Numbers_are_read_the_same_whatever_the_local_culture()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = new CultureInfo("de-DE");
		try
		{
			Share(GamingPc, Numeric("temperature", "21.5"));
			AddEntry(GamingPc, "GAMING-PC", "inst-1", importShared: true);
			await InitializeAsync();
			await RefreshAsync();

			var reading = await _integration.ReadAsync(Declared("gaming_pc_temperature").ResolvedId!);

			Assert.That(reading.Value, Is.EqualTo(21.5m));
		}
		finally
		{
			CultureInfo.CurrentCulture = previous;
		}
	}

	private static DelegateSharedVariable Boolean(string name, string value)
		=> new(name, VariableType.Boolean, value, Present: true, Available: true, CanWrite: false);

	private static DelegateSharedVariable Numeric(string name, string value)
		=> new(name, VariableType.Numeric, value, Present: true, Available: true, CanWrite: false);

	private static DelegateSharedVariable Text(string name, string value)
		=> new(name, VariableType.Text, value, Present: true, Available: true, CanWrite: false);

	private void Share(Uri baseUrl, params DelegateSharedVariable[] variables)
		=> _client.For(baseUrl).SharedVariables.AddRange(variables);

	private Guid AddEntry(Uri baseUrl, string machineName, string instanceId, bool importShared)
		=> _config.AddEntry(machineName,
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[DelegateConfigKeys.BaseUrl] = baseUrl.ToString(),
				[DelegateConfigKeys.Username] = "admin",
				[DelegateConfigKeys.InstanceId] = instanceId,
				[DelegateConfigKeys.MachineName] = machineName,
				[DelegateConfigKeys.InstanceKey] = DelegateSlug.For(machineName, instanceId),
				[DelegateConfigKeys.ConfiguredAt] = _time.Now.ToString("o"),
				[DelegateConfigKeys.ImportSharedVariables] = importShared ? "true" : "false"
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
				{ [DelegateConfigKeys.Password] = "correct-password" });

	private Task InitializeAsync() => _integration.InitializeAsync(new Context(_config, _variables));

	private async Task RefreshAsync()
	{
		foreach (var remote in _manager.Remotes)
		{
			await remote.RefreshSharedVariablesAsync(CancellationToken.None);
		}

		await _integration.RefreshImportsAsync();
	}

	private async Task RegisterDeclaredAsync()
	{
		var owned = OwnedNames();
		foreach (var definition in _integration.Variables.Where(d => !owned.Contains(d.Name!)))
		{
			await _variables.CreateAsync(definition.Name!, definition.Type, definitionId: definition.ResolvedId);
		}
	}

	private string[] Names() => _integration.Variables.Select(v => v.Name!).ToArray();

	private HashSet<string> OwnedNames()
		=> _variables.GetAllAsync().Result.Select(v => v.Name).ToHashSet(StringComparer.Ordinal);

	private VariableDefinition Declared(string name)
	{
		var definition = _integration.Variables.FirstOrDefault(v => v.Name == name);
		Assert.That(definition, Is.Not.Null, $"'{name}' is not declared; declared: {string.Join(", ", Names())}");
		return definition!;
	}

	private static async Task WaitFor(Func<bool> condition)
	{
		for (var i = 0; i < 200 && !condition(); i++)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "the condition was not reached in time");
	}

	private sealed class RecordingInvalidation : IVariablePollingInvalidationSignal
	{
		public List<string> Stale { get; } = [];

		public void MarkStale(string integrationId)
		{
			lock (Stale)
			{
				Stale.Add(integrationId);
			}
		}

		public IReadOnlyCollection<string> DrainStale() => [];
	}

	private sealed class Context : IIntegrationContext
	{
		public Context(IIntegrationConfig config, IVariableApi variables)
		{
			Config = config;
			Variables = variables;
		}

		public IIntegrationConfig Config { get; }

		public IVariableApi Variables { get; }

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
