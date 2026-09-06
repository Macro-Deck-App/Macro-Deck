using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.Adb.Actions;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class ScreenshotActionDefinitionTests
{
	private FakeAdbGateway _gateway = null!;
	private AdbHealthTracker _health = null!;
	private RecordingVariableApi _api = null!;
	private VariableApiAccessor _variables = null!;
	private ScreenshotActionDefinition _action = null!;
	private string _folder = null!;

	[SetUp]
	public void SetUp()
	{
		_gateway = new FakeAdbGateway();
		_health = new AdbHealthTracker();
		_api = new RecordingVariableApi();
		_variables = new VariableApiAccessor { Current = _api };
		_action = new ScreenshotActionDefinition(() => _gateway, _health, _variables);
		_folder = Path.Combine(Path.GetTempPath(), "adb-screenshot-tests-" + Guid.NewGuid().ToString("N"));
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_folder))
		{
			Directory.Delete(_folder, recursive: true);
		}
	}

	private static ActionExecutionContext Context(Dictionary<string, object> parameters) =>
		new() { Parameters = parameters };

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
	public async Task Forwards_the_chosen_device_to_the_gateway()
	{
		await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder, ["device"] = "SERIAL1" }));

		Assert.That(_gateway.Calls, Is.EqualTo(new List<string> { "CaptureScreenshot:SERIAL1" }));
	}

	[Test]
	public async Task Writes_the_full_saved_path_into_the_named_variable()
	{
		await _action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object>
		{
			["folder"] = _folder,
			["fileNameVariable"] = "screenshotPath"
		}));

		var handle = await _api.GetByNameAsync("screenshotPath");

		Assert.That(handle, Is.Not.Null);
		Assert.That((string)handle!.Value!, Does.StartWith(_folder));
	}

	[Test]
	public async Task Without_a_file_name_variable_writes_nothing()
	{
		await _action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.That(_api.CreateCount, Is.EqualTo(0));
	}

	[Test]
	public async Task Without_a_folder_fails_with_invalid_parameter_and_never_calls_the_gateway()
	{
		var result = await _action.CreateExecutor().ExecuteAsync(Context([]));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_gateway.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_disabled_gateway_fails_without_touching_disk()
	{
		_gateway.IsEnabled = false;

		var result = await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[Test]
	public async Task A_gateway_failure_is_reported_and_nothing_is_written_to_disk()
	{
		_gateway.NextResult = AdbGatewayResult.Fail(AdbGatewayFailureCode.DeviceNotFound, "no such device");

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
		_gateway.NextScreenshot = null;

		var result = await _action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object> { ["folder"] = _folder }));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
	}
}
