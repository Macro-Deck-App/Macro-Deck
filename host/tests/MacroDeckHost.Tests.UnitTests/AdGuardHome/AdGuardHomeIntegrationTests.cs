using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome;
using MacroDeckHost.Integrations.AdGuardHome.Actions;
using MacroDeckHost.Tests.UnitTests.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.AdGuardHome;

[TestFixture]
internal sealed class AdGuardHomeIntegrationTests
{
	private FakeAdGuardHomeContext _context = null!;
	private AdGuardHomeHub _hub = null!;
	private Dictionary<string, FakeAdGuardHomeClient> _clients = null!;
	private FakeTimeProvider _time = null!;
	private int _createdClients;
	private AdGuardHomeIntegration _integration = null!;

	[SetUp]
	public void SetUp()
	{
		_context = new FakeAdGuardHomeContext();
		_hub = new AdGuardHomeHub();
		_clients = new Dictionary<string, FakeAdGuardHomeClient>(StringComparer.Ordinal);
		_createdClients = 0;
		_time = new FakeTimeProvider();
		_integration = new AdGuardHomeIntegration(settings =>
		{
			var client = new FakeAdGuardHomeClient();
			_clients[settings.ControlUrl.Host] = client;
			_createdClients++;
			return client;
		}, _time);
		_integration.UseAdGuardHomeSink(_hub);
	}

	[TearDown]
	public Task TearDown() => _integration.ShutdownAsync();

	[Test]
	public async Task Each_instance_is_polled_on_its_own_and_published_to_the_shared_hub()
	{
		AddInstance("Home", "home.lan", "home");
		AddInstance("Office", "office.lan", "office");
		await StartAsync();
		_clients["office.lan"].Status = _clients["office.lan"].Status with { Version = "v0.107.1" };

		await ConnectedAsync(2);

		Assert.Multiple(() =>
		{
			Assert.That(_hub.Instances.Select(instance => instance.Title), Is.EquivalentTo(new[] { "Home", "Office" }));
			Assert.That(_hub.Instances.Single(i => i.Title == "Home").Statistics!.DnsQueries, Is.EqualTo(1000));
			Assert.That(_clients["home.lan"].StatusCalls, Is.GreaterThanOrEqualTo(1));
			Assert.That(_clients["office.lan"].StatusCalls, Is.GreaterThanOrEqualTo(1));
		});
	}

	[Test]
	public async Task Variables_follow_the_stored_key_not_the_title()
	{
		AddInstance("Home DNS", "home.lan", "home");
		await StartAsync();
		await ConnectedAsync(1);

		var names = _integration.Variables.Select(variable => variable.Name).ToList();
		var queries = await _integration.ReadAsync("adguard-home-home-dns-queries");
		var percentage = await _integration.ReadAsync("adguard-home-home-blocked-percentage");

		Assert.Multiple(() =>
		{
			Assert.That(names, Does.Contain("adguard_home_home_protection_enabled"));
			Assert.That(names, Does.Not.Contain("adguard_home_home_dns_is_reachable"));
			Assert.That(queries.Value, Is.EqualTo(1000L));
			Assert.That(percentage.Value, Is.EqualTo(25d));
		});
	}

	[Test]
	public async Task An_unreachable_instance_reports_no_values_and_is_not_reachable()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();
		await ConnectedAsync(1);

		_clients["home.lan"].Failure = AdGuardHomeConnection.Unreachable;
		await _integration.ExecuteAsync(_hub.Instances[0].EntryId,
			new AdGuardHomeCommand(AdGuardHomeCommandKind.EnableProtection),
			CancellationToken.None);
		await CalendarTree.WaitForAsync(() => _hub.Instances[0].Connection == AdGuardHomeConnection.Unreachable,
			"the failure never reached the hub");

		var reachable = await _integration.ReadAsync("adguard-home-home-is-reachable");
		var queries = await _integration.ReadAsync("adguard-home-home-dns-queries");
		var protection = await _integration.ReadAsync("adguard-home-home-protection-enabled");

		Assert.Multiple(() =>
		{
			Assert.That(_hub.Instances[0].Statistics, Is.Null);
			Assert.That(_hub.Instances[0].ProtectionEnabled, Is.Null);
			Assert.That(reachable.Value, Is.EqualTo(false));
			Assert.That(queries.Value, Is.Null);
			Assert.That(protection.Value, Is.Null);
		});
	}

	[Test]
	public async Task A_single_dropped_connection_does_not_take_a_connected_instance_offline()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();
		await ConnectedAsync(1);
		_clients["home.lan"].DroppedStatusRequests = 1;

		await _integration.ExecuteAsync(_hub.Instances[0].EntryId,
			new AdGuardHomeCommand(AdGuardHomeCommandKind.RefreshFilters),
			CancellationToken.None);

		Assert.That(_hub.Instances[0].Connection, Is.EqualTo(AdGuardHomeConnection.Connected));
	}

	[Test]
	public async Task An_action_targets_the_selected_instance_only()
	{
		var office = AddInstance("Office", "office.lan", "office");
		AddInstance("Home", "home.lan", "home");
		await StartAsync();

		var result = await Execute("pause-protection",
			new Dictionary<string, object>
			{
				["instance"] = office.ToString("D"),
				[AdGuardHomeActions.DurationParameter] = "1h"
			});

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.Null);
			Assert.That(_clients["office.lan"].ProtectionCalls, Is.EqualTo(new[] { (false, (TimeSpan?)TimeSpan.FromHours(1)) }));
			Assert.That(_clients["home.lan"].ProtectionCalls, Is.Empty);
		});
	}

	[Test]
	public async Task A_custom_pause_uses_the_entered_duration()
	{
		var home = AddInstance("Home", "home.lan", "home");
		await StartAsync();

		await Execute("pause-protection",
			new Dictionary<string, object>
			{
				["instance"] = home.ToString("D"),
				[AdGuardHomeActions.DurationParameter] = "custom",
				[AdGuardHomeActions.CustomDurationParameter] = 90_000L
			});

		Assert.That(_clients["home.lan"].ProtectionCalls.Single().Pause, Is.EqualTo(TimeSpan.FromSeconds(90)));
	}

	[Test]
	public async Task An_unknown_instance_is_reported_as_not_found()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();

		var result = await Execute("enable-protection",
			new Dictionary<string, object> { ["instance"] = Guid.NewGuid().ToString("D") });

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public async Task Rejected_credentials_fail_the_action_as_rejected_by_the_provider()
	{
		var home = AddInstance("Home", "home.lan", "home");
		await StartAsync();
		_clients["home.lan"].Failure = AdGuardHomeConnection.Unauthorized;

		var result = await Execute("toggle-protection", new Dictionary<string, object> { ["instance"] = home.ToString("D") });

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderRejected));
	}

	[Test]
	public async Task Toggling_filtering_keeps_the_update_interval()
	{
		var home = AddInstance("Home", "home.lan", "home");
		await StartAsync();
		_clients["home.lan"].Filtering = new AdGuardHomeFilteringStatus(true, 72);

		await Execute("toggle-filtering", new Dictionary<string, object> { ["instance"] = home.ToString("D") });

		Assert.That(_clients["home.lan"].FilteringCalls, Is.EqualTo(new[] { (false, 72) }));
	}

	[Test]
	public async Task Refreshing_filters_reaches_the_server()
	{
		var home = AddInstance("Home", "home.lan", "home");
		await StartAsync();

		await Execute("refresh-filters", new Dictionary<string, object> { ["instance"] = home.ToString("D") });

		Assert.That(_clients["home.lan"].RefreshCalls, Is.EqualTo(1));
	}

	[Test]
	public async Task Shutting_down_leaves_no_snapshot_behind()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();
		await ConnectedAsync(1);

		await _integration.ShutdownAsync();

		Assert.That(_hub.Instances, Is.Empty);
	}

	[Test]
	public async Task A_removed_entry_disappears_from_the_hub_on_reload()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();
		await ConnectedAsync(1);

		_context.Clear();
		await _integration.ReloadConfigurationsAsync(CancellationToken.None);

		Assert.That(_hub.Instances, Is.Empty);
	}

	[Test]
	public async Task Adding_an_instance_leaves_the_existing_ones_connected()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();
		await ConnectedAsync(1);
		var states = new global::System.Collections.Concurrent.ConcurrentQueue<AdGuardHomeConnection>();
		_hub.Changed += (_, _) =>
		{
			foreach (var instance in _hub.Instances.Where(i => i.Title == "Home"))
			{
				states.Enqueue(instance.Connection);
			}
		};

		AddInstance("Office", "office.lan", "office");
		await _integration.ReloadConfigurationsAsync(CancellationToken.None);
		var observed = states.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(observed, Is.Not.Empty);
			Assert.That(observed, Has.None.EqualTo(AdGuardHomeConnection.Connecting));
			Assert.That(_createdClients, Is.EqualTo(2), "the existing instance kept its client instead of being rebuilt");
		});
	}

	[Test]
	public async Task A_second_instance_added_later_gets_its_own_variables_without_a_restart()
	{
		AddInstance("Home", "home.lan", "home");
		await StartAsync();

		AddInstance("Office", "office.lan", "office");
		await _integration.ReloadConfigurationsAsync(CancellationToken.None);

		Assert.That(_integration.Variables.Select(variable => variable.Name),
			Does.Contain("adguard_home_office_dns_queries"));
	}

	[Test]
	public async Task The_widget_type_is_registered_when_the_integration_starts()
	{
		var context = new RecordingWidgetTypeContext();

		await _integration.InitializeAsync(context);

		Assert.That(context.Registered.Select(type => type.Id), Is.EqualTo(new[] { AdGuardHomeWidgetType.LocalId }));
	}

	private Guid AddInstance(string title, string host, string key)
		=> _context.AddEntry(title,
			new Dictionary<string, string?>
			{
				[AdGuardHomeConfigKeys.BaseUrl] = $"http://{host}:3000",
				[AdGuardHomeConfigKeys.Username] = "admin",
				[AdGuardHomeConfigKeys.VariableKey] = key,
			},
			"secret");

	private Task StartAsync() => _integration.InitializeAsync(_context);

	private Task ConnectedAsync(int count)
		=> CalendarTree.WaitForAsync(() => _hub.Instances.Count(instance => instance.IsConnected) == count,
			"the instances never connected");

	private Task<ActionResult> Execute(string actionId, Dictionary<string, object> parameters)
		=> _integration.Actions.Single(action => action.Id == actionId)
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = parameters });

	private sealed class RecordingWidgetTypeContext : IWidgetTypeProviderContext
	{
		public List<WidgetTypeDescriptor> Registered { get; } = [];

		public Task<WidgetTypeRegistration> RegisterWidgetTypeAsync(
			WidgetTypeDescriptor widgetType,
			CancellationToken cancellationToken = default)
		{
			Registered.Add(widgetType);
			return Task.FromResult(new WidgetTypeRegistration(widgetType.Id, AdGuardHomeIntegration.IntegrationId));
		}

		public Task UnregisterWidgetTypeAsync(string widgetTypeId, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}
