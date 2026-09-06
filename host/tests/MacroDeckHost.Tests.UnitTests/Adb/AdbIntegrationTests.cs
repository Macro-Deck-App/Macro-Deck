using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbIntegrationTests
{
	private FakeAdbGateway _gateway = null!;
	private AdbIntegration _integration = null!;
	private RecordingPublisher _publisher = null!;

	[SetUp]
	public void SetUp()
	{
		_gateway = new FakeAdbGateway();
		_integration = new AdbIntegration();
		_publisher = new RecordingPublisher();
	}

	[TearDown]
	public void TearDown() => _integration.Dispose();

	private static AdbGatewayDevice Device(
		string serial = "ABC123",
		AdbGatewayDeviceState state = AdbGatewayDeviceState.Device,
		string? model = null)
		=> new(serial, state, model, Manufacturer: null, TunnelEstablished: true);

	private Task Initialize() => _integration.InitializeAsync(new FakeContext { Events = _publisher });

	[Test]
	public void The_integration_is_identified()
	{
		var attribute = (MacroDeckIntegrationAttribute)typeof(AdbIntegration)
			.GetCustomAttributes(typeof(MacroDeckIntegrationAttribute), false)
			.Single();

		Assert.Multiple(() =>
		{
			Assert.That(_integration.Id, Is.EqualTo("app.macro-deck.adb"));
			Assert.That(TestLocalization.Resolve(_integration.Name), Is.Not.Empty);
			Assert.That(attribute.EnabledByDefault, Is.False);
		});
	}

	[Test]
	public void The_integration_ships_an_icon()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.IconMimeType, Is.EqualTo("image/svg+xml"));
			Assert.That(_integration.GetIcon(), Is.Not.Empty);
		});
	}

	[Test]
	public void The_integration_registers_cleanly_through_the_capability_validator()
	{
		var conflicts = IntegrationCapabilityValidator.Validate(_integration);

		Assert.That(conflicts, Is.Empty);
	}

	[Test]
	public void Action_and_event_ids_are_unique()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.Actions.Select(action => action.Id), Is.Unique);
			Assert.That(_integration.EventDefinitions.Select(definition => definition.Id), Is.Unique);
		});
	}

	[Test]
	public void Every_event_filter_matches_a_payload_parameter()
	{
		Assert.Multiple(() =>
		{
			foreach (var definition in _integration.EventDefinitions)
			{
				var payload = definition.PayloadParameters.Select(parameter => parameter.Name).ToList();
				foreach (var configuration in definition.ConfigurationParameters)
				{
					Assert.That(payload, Does.Contain(configuration.Name), $"{definition.Id}.{configuration.Name}");
				}
			}
		});
	}

	[Test]
	public void DeclaredVariables_exposes_tier_one_plus_the_tier_two_template_and_depends_on_configuration()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_integration.VariablesDependOnConfiguration, Is.True);
			Assert.That(_integration.DeclaredVariables, Is.SupersetOf(AdbVariables.All));
			Assert.That(_integration.DeclaredVariables, Is.SupersetOf(AdbVariables.DeviceTemplates));
			Assert.That(_integration.Variables, Is.EqualTo(AdbVariables.All));
		});
	}

	[Test]
	public async Task GetIssuesAsync_before_a_gateway_is_bound_reports_nothing()
	{
		await Initialize();

		// UseGateway was never called on this instance - the defensive path a badge poll could still
		// hit if it races the host's own startup binding.
		Assert.That(await _integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task GetIssuesAsync_reports_when_ADB_is_turned_off()
	{
		_gateway.IsEnabled = false;
		_integration.UseGateway(_gateway);
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues.Select(issue => issue.Id), Does.Contain(AdbIntegration.DisabledIssueId));
			Assert.That(issues.Single(issue => issue.Id == AdbIntegration.DisabledIssueId).Severity,
				Is.EqualTo(IntegrationIssueSeverity.Warning));
		});
	}

	[Test]
	public async Task GetIssuesAsync_reports_no_devices_connected()
	{
		_integration.UseGateway(_gateway);
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Contain(AdbIntegration.NoDevicesIssueId));
	}

	[Test]
	public async Task GetIssuesAsync_does_not_report_no_devices_when_one_is_connected()
	{
		_gateway.Devices = [Device()];
		_integration.UseGateway(_gateway);
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Not.Contain(AdbIntegration.NoDevicesIssueId));
	}

	[Test]
	public async Task GetIssuesAsync_reports_one_issue_per_unauthorized_device()
	{
		_gateway.Devices =
		[
			Device("S1", AdbGatewayDeviceState.Unauthorized),
			Device("S2", AdbGatewayDeviceState.NoPermissions),
			Device("S3", AdbGatewayDeviceState.Device)
		];
		_integration.UseGateway(_gateway);
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		Assert.Multiple(() =>
		{
			Assert.That(issues.Select(issue => issue.Id),
				Does.Contain($"{AdbIntegration.UnauthorizedIssuePrefix}S1"));
			Assert.That(issues.Select(issue => issue.Id),
				Does.Contain($"{AdbIntegration.UnauthorizedIssuePrefix}S2"));
			Assert.That(issues.Select(issue => issue.Id),
				Does.Not.Contain($"{AdbIntegration.UnauthorizedIssuePrefix}S3"));
		});
	}

	[Test]
	public async Task GetIssuesAsync_reports_executable_not_found_after_a_failing_action()
	{
		_gateway.NextResult = AdbGatewayResult.Fail(AdbGatewayFailureCode.ExecutableNotFound, "not found");
		_integration.UseGateway(_gateway);
		await Initialize();

		await RunWakeAction();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Contain(AdbIntegration.ExecutableNotFoundIssueId));
	}

	[Test]
	public async Task GetIssuesAsync_clears_executable_not_found_after_a_successful_action()
	{
		_gateway.NextResult = AdbGatewayResult.Fail(AdbGatewayFailureCode.ExecutableNotFound, "not found");
		_integration.UseGateway(_gateway);
		await Initialize();
		await RunWakeAction();

		_gateway.NextResult = AdbGatewayResult.Ok();
		await RunWakeAction();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Not.Contain(AdbIntegration.ExecutableNotFoundIssueId));
	}

	[Test]
	public async Task ResolveIssueAsync_reports_failure_for_every_issue()
	{
		var resolution = await _integration.ResolveIssueAsync(AdbIntegration.DisabledIssueId);

		Assert.That(resolution.Success, Is.False);
	}

	[Test]
	public async Task A_device_change_publishes_exactly_one_occurrence()
	{
		_integration.UseGateway(_gateway);
		await Initialize();

		_gateway.RaiseDeviceChanged(AdbGatewayDeviceChangeKind.Connected, Device());

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Reinitializing_without_shutting_down_first_still_delivers_exactly_one_occurrence()
	{
		_integration.UseGateway(_gateway);
		await Initialize();
		_integration.UseGateway(_gateway);
		await Initialize();

		_gateway.RaiseDeviceChanged(AdbGatewayDeviceChangeKind.Connected, Device());

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Shutdown_then_reinitialize_delivers_exactly_one_occurrence()
	{
		_integration.UseGateway(_gateway);
		await Initialize();
		await _integration.ShutdownAsync();

		_integration.UseGateway(_gateway);
		await Initialize();

		_gateway.RaiseDeviceChanged(AdbGatewayDeviceChangeKind.Connected, Device());

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Shutdown_stops_delivering_events()
	{
		_integration.UseGateway(_gateway);
		await Initialize();
		await _integration.ShutdownAsync();

		_gateway.RaiseDeviceChanged(AdbGatewayDeviceChangeKind.Connected, Device());

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public async Task Dispose_is_idempotent()
	{
		_integration.UseGateway(_gateway);
		await Initialize();

		Assert.DoesNotThrow(() =>
		{
			_integration.Dispose();
			_integration.Dispose();
		});
	}

	[Test]
	public async Task Dispose_stops_delivering_events()
	{
		_integration.UseGateway(_gateway);
		await Initialize();

		_integration.Dispose();
		_gateway.RaiseDeviceChanged(AdbGatewayDeviceChangeKind.Connected, Device());

		Assert.That(_publisher.Published, Is.Empty);
	}

	private async Task RunWakeAction()
	{
		var wake = _integration.Actions.Single(action => action.Id == "wake");
		await wake.CreateExecutor().ExecuteAsync(new ActionExecutionContext
			{ Parameters = new Dictionary<string, object>() });
	}

	private sealed class FakeContext : IIntegrationContext
	{
		public IVariableApi Variables { get; init; } = new RecordingVariableApi();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events { get; init; } = new RecordingPublisher();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}
}
