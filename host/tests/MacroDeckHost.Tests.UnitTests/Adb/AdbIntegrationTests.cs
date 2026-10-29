using MacroDeckHost.Integrations.Adb.Variables;
using MacroDeckHost.Localization;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;
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
	private ScriptedAndroidDevices _devices = null!;
	private AdbIntegration _integration = null!;
	private RecordingPublisher _publisher = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new ScriptedAndroidDevices();
		_integration = new AdbIntegration();
		_publisher = new RecordingPublisher();
	}

	[TearDown]
	public void TearDown()
	{
		_integration.Dispose();
	}

	private Task Initialize()
	{
		return _integration.InitializeAsync(new FakeContext { Events = _publisher, AndroidDevices = _devices });
	}

	[Test]
	public void Actions_that_reach_outside_macro_deck_declare_their_risk_category()
	{
		var categories = _integration.Actions.ToDictionary(action => action.Id, action => action.RiskCategory);

		Assert.Multiple(() =>
		{
			Assert.That(categories["start-app"], Is.EqualTo(ActionRiskCategory.Programs));
			Assert.That(categories["input-text"], Is.EqualTo(ActionRiskCategory.Input));
			Assert.That(categories["key-event"], Is.EqualTo(ActionRiskCategory.Input));
			Assert.That(categories["reboot"], Is.EqualTo(ActionRiskCategory.Power));
			Assert.That(categories["wake"], Is.EqualTo(ActionRiskCategory.None));
		});
	}

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
	public async Task GetIssuesAsync_before_initialization_reports_nothing()
	{
		Assert.That(await _integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task GetIssuesAsync_reports_when_ADB_is_turned_off()
	{
		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);
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
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Contain(AdbIntegration.NoDevicesIssueId));
	}

	[Test]
	public async Task GetIssuesAsync_does_not_report_no_devices_when_one_is_connected()
	{
		_devices.Attach("ABC123", null);
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Not.Contain(AdbIntegration.NoDevicesIssueId));
	}

	[Test]
	public async Task GetIssuesAsync_reports_one_issue_per_unauthorized_device()
	{
		_devices.Attach("S1", null, state: AndroidDeviceState.Unauthorized);
		_devices.Attach("S2", null, state: AndroidDeviceState.Unauthorized, lacksUsbPermission: true);
		_devices.Attach("S3", null, state: AndroidDeviceState.Online);
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
	public async Task A_device_lacking_usb_permission_gets_the_usb_permission_description()
	{
		_devices.Attach("S1", null, state: AndroidDeviceState.Unauthorized);
		_devices.Attach("S2", null, state: AndroidDeviceState.Unauthorized, lacksUsbPermission: true);
		await Initialize();

		var issues = await _integration.GetIssuesAsync();

		string? Description(string serial)
		{
			return TestLocalization.Resolve(issues
				.Single(issue => issue.Id == $"{AdbIntegration.UnauthorizedIssuePrefix}{serial}")
				.Description);
		}

		Assert.Multiple(() =>
		{
			Assert.That(Description("S2"),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Adb.Issues.NoPermissionsDescription())));
			Assert.That(Description("S1"),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Adb.Issues.UnauthorizedDescription())));
		});
	}

	[Test]
	public async Task GetIssuesAsync_reports_executable_not_found_after_a_failing_action()
	{
		_devices.Attach("ABC123", null).NextFailure =
			new AndroidDeviceException(AndroidDeviceErrorCode.AdbUnavailable, "not found");
		await Initialize();

		await RunWakeAction();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Contain(AdbIntegration.ExecutableNotFoundIssueId));
	}

	[Test]
	public async Task Without_adb_and_without_any_device_an_action_still_reports_executable_not_found()
	{
		_devices.IsAdbUnavailable = true;
		await Initialize();

		await RunWakeAction();

		var issues = await _integration.GetIssuesAsync();

		Assert.That(issues.Select(issue => issue.Id), Does.Contain(AdbIntegration.ExecutableNotFoundIssueId));
	}

	[Test]
	public async Task GetIssuesAsync_clears_executable_not_found_after_a_successful_action()
	{
		var device = _devices.Attach("ABC123", null);
		device.NextFailure = new AndroidDeviceException(AndroidDeviceErrorCode.AdbUnavailable, "not found");
		await Initialize();
		await RunWakeAction();

		device.NextFailure = null;
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
		await Initialize();

		_devices.Attach("ABC123", null);

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Reinitializing_without_shutting_down_first_still_delivers_exactly_one_occurrence()
	{
		await Initialize();
		await Initialize();

		_devices.Attach("ABC123", null);

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Shutdown_then_reinitialize_delivers_exactly_one_occurrence()
	{
		await Initialize();
		await _integration.ShutdownAsync();
		await Initialize();

		_devices.Attach("ABC123", null);

		Assert.That(_publisher.Published, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Shutdown_stops_delivering_events()
	{
		await Initialize();
		await _integration.ShutdownAsync();

		_devices.Attach("ABC123", null);

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public async Task Dispose_is_idempotent()
	{
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
		await Initialize();

		_integration.Dispose();
		_devices.Attach("ABC123", null);

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public async Task The_integration_reaches_adb_only_through_its_context()
	{
		_devices.Attach("ABC123", null);
		await Initialize();

		await RunWakeAction();

		Assert.That(_devices.Calls, Is.EqualTo(new[] { "SendKey:ABC123:Wakeup" }));
	}

	private async Task RunWakeAction()
	{
		var wake = _integration.Actions.Single(action => action.Id == "wake");
		await wake.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
				{ Parameters = new Dictionary<string, object>() });
	}

	private sealed class FakeContext : IIntegrationContext
	{
		public IVariableApi Variables { get; init; } = new RecordingVariableApi();

		public IUserVariableApi UserVariables => null!;

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events { get; init; } = new RecordingPublisher();

		public IUserNotifier Notifications => throw new NotSupportedException();

		public IAndroidDeviceManager AndroidDevices { get; init; } = UnsupportedAndroidDeviceManager.Instance;
	}
}
