using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class GetJsonValueActionTests
{
	private FakeHttpRequestClient _client = null!;
	private RecordingVariableApi _variables = null!;
	private IActionExecutor _executor = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeHttpRequestClient();
		_variables = new RecordingVariableApi();
		var accessor = new HttpVariableAccessor { Current = _variables };
		_executor = new GetJsonValueActionDefinition(_client, accessor).CreateExecutor();
	}

	[Test]
	public async Task The_happy_path_writes_the_target_variable_with_the_right_type()
	{
		_client.Enqueue(Succeeded(200, """{"name":"Ada"}"""));

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("path", "$.name"),
			("variable", "target")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_variables.Written["target"], Is.EqualTo("Ada"));
		});
	}

	[Test]
	public async Task A_path_miss_fails_with_not_found()
	{
		_client.Enqueue(Succeeded(200, "{}"));

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("path", "$.missing"),
			("variable", "target")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(_variables.Written, Does.Not.ContainKey("target"));
		});
	}

	[Test]
	public async Task A_non_2xx_response_fails()
	{
		_client.Enqueue(Succeeded(404, "{}"));

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("path", "$"),
			("variable", "target")));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task A_non_json_body_fails()
	{
		_client.Enqueue(Succeeded(200, "not json"));

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("path", "$"),
			("variable", "target")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task A_blank_target_variable_fails_without_calling_the_client()
	{
		var result = await Execute(Parameters(("url", "https://example.invalid/")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_client.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_variable_the_host_refuses_fails_the_action()
	{
		_client.Enqueue(Succeeded(200, """{"name":"Ada"}"""));
		_variables.FailWrites = true;

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("path", "$.name"),
			("variable", "target")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task An_unavailable_variable_api_fails_the_action()
	{
		_client.Enqueue(Succeeded(200, """{"name":"Ada"}"""));
		var executor = new GetJsonValueActionDefinition(_client, new HttpVariableAccessor()).CreateExecutor();

		var result = await executor.ExecuteAsync(new ActionExecutionContext
		{
			Parameters = Parameters(("url", "https://example.invalid/"),
				("path", "$.name"),
				("variable", "target"))
		});

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	private Task<ActionResult> Execute(Dictionary<string, object> parameters)
		=> _executor.ExecuteAsync(new ActionExecutionContext { Parameters = parameters });

	private static HttpSendOutcome Succeeded(int statusCode, string body)
		=> HttpSendOutcome.Succeeded(new HttpResponseSnapshot(statusCode,
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
			body,
			false,
			"application/json",
			5));

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
