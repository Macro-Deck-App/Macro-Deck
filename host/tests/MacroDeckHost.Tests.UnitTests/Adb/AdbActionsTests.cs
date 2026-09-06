using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Adb.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbActionsTests
{
	private FakeAdbGateway _gateway = null!;
	private AdbHealthTracker _health = null!;
	private VariableApiAccessor _variables = null!;
	private IReadOnlyList<IActionDefinition> _actions = null!;

	[SetUp]
	public void SetUp()
	{
		_gateway = new FakeAdbGateway();
		_health = new AdbHealthTracker();
		_variables = new VariableApiAccessor();
		_actions = AdbActions.Create(() => _gateway, _health, _variables);
	}

	private static ActionExecutionContext Context(Dictionary<string, object> parameters) =>
		new() { Parameters = parameters };

	private IActionDefinition Action(string id) => _actions.Single(action => action.Id == id);

	[Test]
	public void Eleven_actions_are_registered()
	{
		Assert.That(_actions, Has.Count.EqualTo(11));
	}

	[Test]
	public async Task Wake_sends_the_wakeup_key()
	{
		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "SendKey::Wakeup" }));
		});
	}

	[Test]
	public async Task Sleep_sends_the_sleep_key()
	{
		await Action("sleep").CreateExecutor().ExecuteAsync(Context([]));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "SendKey::Sleep" }));
	}

	[Test]
	public async Task StartApp_forwards_the_device_and_package()
	{
		await Action("start-app").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
				{ ["device"] = "SERIAL1", ["package"] = "com.example.app" }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "StartApp:SERIAL1:com.example.app" }));
	}

	[Test]
	public async Task StartApp_without_a_package_fails_without_calling_the_gateway()
	{
		var result = await Action("start-app").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task StopApp_forwards_the_package()
	{
		await Action("stop-app").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["package"] = "com.example.app" }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "ForceStopApp::com.example.app" }));
	}

	[Test]
	public async Task StopApp_without_a_package_fails_without_calling_the_gateway()
	{
		var result = await Action("stop-app").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task OpenUri_forwards_the_uri()
	{
		await Action("open-uri").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["uri"] = "https://example.com" }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "OpenUri::https://example.com" }));
	}

	[Test]
	public async Task OpenUri_without_a_uri_fails_without_calling_the_gateway()
	{
		var result = await Action("open-uri").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task InputText_forwards_the_text()
	{
		await Action("input-text").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["text"] = "hello world" }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "InputText::hello world" }));
	}

	[Test]
	public async Task InputText_without_text_fails_without_calling_the_gateway()
	{
		var result = await Action("input-text").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task KeyEvent_parses_the_chosen_key()
	{
		await Action("key-event").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["key"] = nameof(AdbGatewayKey.VolumeUp) }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "SendKey::VolumeUp" }));
	}

	[Test]
	public async Task KeyEvent_with_an_unrecognized_key_fails_with_invalid_parameter()
	{
		var result = await Action("key-event").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["key"] = "NotARealKey" }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Every_AdbGatewayKey_is_offered_as_a_choice_option()
	{
		var keyParameter = Action("key-event").Parameters.Single(parameter => parameter.Name == "key");

		Assert.That(keyParameter.Options!.Select(option => option.Value),
			Is.EquivalentTo(Enum.GetValues<AdbGatewayKey>().Select(key => key.ToString())));
	}

	[Test]
	public async Task Tap_forwards_the_coordinates()
	{
		await Action("tap").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["x"] = 100, ["y"] = 200 }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "Tap::100:200" }));
	}

	[Test]
	public async Task Swipe_forwards_every_coordinate_and_the_duration()
	{
		await Action("swipe").CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["x1"] = 10,
			["y1"] = 20,
			["x2"] = 30,
			["y2"] = 40,
			["durationMs"] = 500
		}));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "Swipe::10:20:30:40:500" }));
	}

	[Test]
	public async Task Swipe_without_a_duration_defaults_to_300ms()
	{
		await Action("swipe").CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["x1"] = 10,
			["y1"] = 20,
			["x2"] = 30,
			["y2"] = 40
		}));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "Swipe::10:20:30:40:300" }));
	}

	[Test]
	public async Task Reboot_forwards_the_chosen_mode()
	{
		await Action("reboot").CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["mode"] = nameof(AdbGatewayRebootMode.Recovery) }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "Reboot::Recovery" }));
	}

	[Test]
	public async Task Reboot_without_a_mode_defaults_to_normal()
	{
		await Action("reboot").CreateExecutor().ExecuteAsync(Context([]));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "Reboot::Normal" }));
	}

	[TestCase(AdbGatewayFailureCode.Disabled, ActionErrorCodes.Unavailable)]
	[TestCase(AdbGatewayFailureCode.ExecutableNotFound, ActionErrorCodes.Unavailable)]
	[TestCase(AdbGatewayFailureCode.ServerUnreachable, ActionErrorCodes.NotConnected)]
	[TestCase(AdbGatewayFailureCode.DeviceNotFound, ActionErrorCodes.NotFound)]
	[TestCase(AdbGatewayFailureCode.DeviceUnauthorized, ActionErrorCodes.PermissionDenied)]
	[TestCase(AdbGatewayFailureCode.DeviceOffline, ActionErrorCodes.NotConnected)]
	[TestCase(AdbGatewayFailureCode.Timeout, ActionErrorCodes.Timeout)]
	[TestCase(AdbGatewayFailureCode.InvalidParameter, ActionErrorCodes.InvalidParameter)]
	[TestCase(AdbGatewayFailureCode.CommandFailed, ActionErrorCodes.ProviderError)]
	[TestCase(AdbGatewayFailureCode.Unsupported, ActionErrorCodes.Unavailable)]
	public async Task A_gateway_failure_code_maps_to_the_expected_action_error_code(
		AdbGatewayFailureCode failure,
		string expectedErrorCode)
	{
		_gateway.NextResult = AdbGatewayResult.Fail(failure, "something went wrong");

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(expectedErrorCode));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo("something went wrong"));
		});
	}

	[Test]
	public void A_null_gateway_fails_with_unavailable_and_never_throws()
	{
		var actions = AdbActions.Create(() => null, _health, _variables);
		var action = actions.Single(candidate => candidate.Id == "wake");

		ActionResult? result = null;
		Assert.DoesNotThrowAsync(async () => result = await action.CreateExecutor().ExecuteAsync(Context([])));

		Assert.Multiple(() =>
		{
			Assert.That(result!.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
		});
	}

	[Test]
	public async Task A_disabled_gateway_fails_with_unavailable_and_never_calls_the_gateway()
	{
		_gateway.IsEnabled = false;

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo("ADB is turned off in Settings > ADB."));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public void Every_action_declares_the_device_picker_as_its_first_parameter()
	{
		Assert.Multiple(() =>
		{
			foreach (var action in _actions)
			{
				Assert.That(action.Parameters, Is.Not.Empty, action.Id);
				Assert.That(action.Parameters[0].Name, Is.EqualTo("device"), action.Id);
				Assert.That(action.Parameters[0].Required, Is.False, action.Id);
			}
		});
	}
}
