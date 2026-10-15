using MacroDeckHost.Integrations.Adb.Actions;
using MacroDeckHost.Integrations.Adb.State;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbActionsTests
{
	private ScriptedAndroidDevices _devices = null!;
	private AdbHealthTracker _health = null!;
	private VariableApiAccessor _variables = null!;
	private IReadOnlyList<IActionDefinition> _actions = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new ScriptedAndroidDevices();
		_devices.Attach("DEFAULT", null);
		_devices.DefaultSerial = "DEFAULT";
		_health = new AdbHealthTracker();
		_variables = new VariableApiAccessor();
		_actions = AdbActions.Create(new AdbActionServices(() => _devices, () => null, _health, _variables));
	}

	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
	{
		return new ActionExecutionContext { Parameters = parameters };
	}

	private IActionDefinition Action(string id)
	{
		return _actions.Single(action => action.Id == id);
	}

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
			Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "SendKey:DEFAULT:Wakeup" }));
		});
	}

	[Test]
	public async Task Sleep_sends_the_sleep_key()
	{
		await Action("sleep").CreateExecutor().ExecuteAsync(Context([]));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "SendKey:DEFAULT:Sleep" }));
	}

	[Test]
	public async Task StartApp_forwards_the_device_and_package()
	{
		_devices.Attach("SERIAL1", null);

		await Action("start-app")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
				{ ["device"] = "SERIAL1", ["package"] = "com.example.app" }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "StartApp:SERIAL1:com.example.app" }));
	}

	[Test]
	public async Task StartApp_without_a_package_fails_without_calling_the_device()
	{
		var result = await Action("start-app").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task StopApp_forwards_the_package()
	{
		await Action("stop-app")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["package"] = "com.example.app" }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "ForceStopApp:DEFAULT:com.example.app" }));
	}

	[Test]
	public async Task StopApp_without_a_package_fails_without_calling_the_device()
	{
		var result = await Action("stop-app").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task OpenUri_forwards_the_uri()
	{
		await Action("open-uri")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["uri"] = "https://example.com" }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "OpenUri:DEFAULT:https://example.com" }));
	}

	[Test]
	public async Task OpenUri_without_a_uri_fails_without_calling_the_device()
	{
		var result = await Action("open-uri").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task InputText_forwards_the_text()
	{
		await Action("input-text")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["text"] = "hello world" }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "InputText:DEFAULT:hello world" }));
	}

	[Test]
	public async Task InputText_without_text_fails_without_calling_the_device()
	{
		var result = await Action("input-text").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task KeyEvent_parses_the_chosen_key()
	{
		await Action("key-event")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["key"] = nameof(AndroidKey.VolumeUp) }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "SendKey:DEFAULT:VolumeUp" }));
	}

	[Test]
	public async Task KeyEvent_with_an_unrecognized_key_fails_with_invalid_parameter()
	{
		var result = await Action("key-event")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["key"] = "NotARealKey" }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Every_AndroidKey_is_offered_as_a_choice_option()
	{
		var keyParameter = Action("key-event").Parameters.Single(parameter => parameter.Name == "key");

		Assert.That(keyParameter.Options!.Select(option => option.Value),
			Is.EquivalentTo(Enum.GetValues<AndroidKey>().Select(key => key.ToString())));
	}

	[Test]
	public async Task Tap_forwards_the_coordinates()
	{
		await Action("tap")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["x"] = 100, ["y"] = 200 }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "Tap:DEFAULT:100:200" }));
	}

	[Test]
	public async Task Swipe_forwards_every_coordinate_and_the_duration()
	{
		await Action("swipe")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
			{
				["x1"] = 10,
				["y1"] = 20,
				["x2"] = 30,
				["y2"] = 40,
				["durationMs"] = 500
			}));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "Swipe:DEFAULT:10:20:30:40:500" }));
	}

	[Test]
	public async Task Swipe_without_a_duration_defaults_to_300ms()
	{
		await Action("swipe")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
			{
				["x1"] = 10,
				["y1"] = 20,
				["x2"] = 30,
				["y2"] = 40
			}));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "Swipe:DEFAULT:10:20:30:40:300" }));
	}

	[Test]
	public async Task Reboot_forwards_the_chosen_mode()
	{
		await Action("reboot")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["mode"] = nameof(AndroidRebootMode.Recovery) }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "Reboot:DEFAULT:Recovery" }));
	}

	[Test]
	public async Task Reboot_without_a_mode_defaults_to_normal()
	{
		await Action("reboot").CreateExecutor().ExecuteAsync(Context([]));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "Reboot:DEFAULT:Normal" }));
	}

	[TestCase(AndroidDeviceErrorCode.AdbNotEnabled, ActionErrorCodes.Unavailable)]
	[TestCase(AndroidDeviceErrorCode.AdbUnavailable, ActionErrorCodes.Unavailable)]
	[TestCase(AndroidDeviceErrorCode.DeviceNotFound, ActionErrorCodes.NotFound)]
	[TestCase(AndroidDeviceErrorCode.DeviceUnauthorized, ActionErrorCodes.PermissionDenied)]
	[TestCase(AndroidDeviceErrorCode.DeviceOffline, ActionErrorCodes.NotConnected)]
	[TestCase(AndroidDeviceErrorCode.Timeout, ActionErrorCodes.Timeout)]
	[TestCase(AndroidDeviceErrorCode.InvalidArgument, ActionErrorCodes.InvalidParameter)]
	[TestCase(AndroidDeviceErrorCode.CommandFailed, ActionErrorCodes.ProviderError)]
	[TestCase(AndroidDeviceErrorCode.Unsupported, ActionErrorCodes.Unavailable)]
	public async Task A_device_failure_code_maps_to_the_expected_action_error_code(
		AndroidDeviceErrorCode failure,
		string expectedErrorCode)
	{
		Device("DEFAULT").NextFailure = new AndroidDeviceException(failure, "something went wrong");

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(expectedErrorCode));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo("something went wrong"));
		});
	}

	[Test]
	public async Task Without_a_chosen_or_default_device_the_action_fails_with_not_found_and_runs_nothing()
	{
		_devices.DefaultSerial = null;
		_devices.Attach("OTHER", null);

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_configured_default_device_not_seen_yet_fails_as_the_adb_manager_reported_it()
	{
		_devices.DefaultSerial = "NEVER-SEEN";

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo("No default device is configured and no single device is connected."));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task Without_adb_the_action_fails_with_unavailable_even_when_no_device_was_ever_seen()
	{
		_devices.Detach("DEFAULT");
		_devices.DefaultSerial = null;
		_devices.IsAdbUnavailable = true;

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo("The adb executable could not be located."));
			Assert.That(_health.LastFailure, Is.EqualTo(AndroidDeviceErrorCode.AdbUnavailable));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_chosen_device_that_is_not_attached_fails_with_not_found()
	{
		var result = await Action("wake")
			.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["device"] = "GONE" }));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
	}

	[Test]
	public void Before_the_integration_has_android_devices_an_action_fails_with_unavailable_and_never_throws()
	{
		var actions = AdbActions.Create(new AdbActionServices(() => null, () => null, _health, _variables));
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
	public async Task With_adb_turned_off_an_action_fails_with_unavailable_and_never_calls_a_device()
	{
		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);

		var result = await Action("wake").CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo("ADB is turned off in Settings > ADB."));
			Assert.That(_devices.Calls, Is.Empty);
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

	private ScriptedAndroidDevice Device(string serial)
	{
		return (ScriptedAndroidDevice)_devices.FindDevice(serial)!;
	}
}
