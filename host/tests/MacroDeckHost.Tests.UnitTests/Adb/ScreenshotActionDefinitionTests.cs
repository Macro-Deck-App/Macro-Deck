using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Adb.Actions;
using MacroDeckHost.Integrations.Adb.State;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Android;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class ScreenshotActionDefinitionTests
{
	private ScriptedAndroidDevices _devices = null!;
	private AdbHealthTracker _health = null!;
	private ActionVariableTargets _targets = null!;
	private VariableApiAccessor _variables = null!;
	private ScreenshotActionDefinition _action = null!;
	private string _folder = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new ScriptedAndroidDevices();
		_devices.Attach("DEFAULT", null);
		_health = new AdbHealthTracker();
		_targets = new ActionVariableTargets(AdbIntegration.IntegrationId);
		_variables = new VariableApiAccessor
		{
			Current = _targets.IntegrationVariables,
			UserVariables = _targets.UserVariables
		};
		_action = new ScreenshotActionDefinition(new AdbActionServices(() => _devices,
			() => null,
			_health,
			_variables));
		_folder = Path.Combine(Path.GetTempPath(), "adb-screenshot-tests-" + Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown()
	{
		_targets.Dispose();
		if (Directory.Exists(_folder))
		{
			Directory.Delete(_folder, true);
		}
	}

	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
	{
		return new ActionExecutionContext { Parameters = parameters };
	}

	[Test]
	public async Task Saves_the_capture_as_a_png_in_the_chosen_folder()
	{
		var result = await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(Directory.Exists(_folder), Is.True);
			Assert.That(Directory.GetFiles(_folder, "*.png"), Has.Length.EqualTo(1));
		});
	}

	[Test]
	public async Task Creates_the_folder_when_it_does_not_exist_yet()
	{
		Assert.That(Directory.Exists(_folder), Is.False);

		await _action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.That(Directory.Exists(_folder), Is.True);
	}

	[Test]
	public async Task Captures_the_chosen_device()
	{
		_devices.Attach("SERIAL1", null);

		await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder, ["device"] = "SERIAL1" }));

		Assert.That(_devices.Calls, Is.EqualTo(new List<string> { "CaptureScreenshot:SERIAL1" }));
	}

	[Test]
	public async Task Writes_the_full_saved_path_into_the_named_variable()
	{
		await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>
			{
				["folder"] = _folder,
				["fileNameVariable"] = "screenshot_path"
			}));

		var value = await _targets.ValueOf("screenshot_path");

		Assert.That(value, Is.Not.Null);
		Assert.That((string)value!, Does.StartWith(_folder));
	}

	[Test]
	public async Task Without_a_file_name_variable_writes_nothing()
	{
		await _action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.That(await _targets.Service.GetAll(), Is.Empty);
	}

	[Test]
	public async Task Without_a_folder_fails_with_invalid_parameter_and_never_calls_a_device()
	{
		var result = await _action.CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_devices.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task With_adb_turned_off_it_fails_without_touching_disk()
	{
		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);

		var result = await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[Test]
	public async Task A_device_failure_is_reported_and_nothing_is_written_to_disk()
	{
		((ScriptedAndroidDevice)_devices.FindDevice("DEFAULT")!).NextFailure =
			new AndroidDeviceException(AndroidDeviceErrorCode.DeviceNotFound, "no such device");

		var result = await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[Test]
	public async Task No_screenshot_data_is_reported_as_a_provider_error()
	{
		((ScriptedAndroidDevice)_devices.FindDevice("DEFAULT")!).Screenshot = [];

		var result = await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
	}
}
