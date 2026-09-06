using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class SendRequestActionTests
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
		_executor = new SendRequestActionDefinition(_client, accessor).CreateExecutor();
	}

	[Test]
	public async Task A_200_response_with_the_default_2xx_expectation_succeeds()
	{
		_client.Enqueue(Succeeded(200));

		var result = await Execute(Parameters(("url", "https://example.invalid/")));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task A_404_response_with_the_default_2xx_expectation_fails_as_not_found()
	{
		_client.Enqueue(Succeeded(404));

		var result = await Execute(Parameters(("url", "https://example.invalid/")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
		});
	}

	[Test]
	public async Task A_404_response_matching_a_widened_expected_status_succeeds()
	{
		_client.Enqueue(Succeeded(404));

		var result = await Execute(Parameters(("url", "https://example.invalid/"), ("expectedStatus", "200,404")));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task A_500_response_fails_as_a_provider_error()
	{
		_client.Enqueue(Succeeded(500));

		var result = await Execute(Parameters(("url", "https://example.invalid/")));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
	}

	[Test]
	public async Task A_401_response_fails_as_permission_denied()
	{
		_client.Enqueue(Succeeded(401));

		var result = await Execute(Parameters(("url", "https://example.invalid/")));

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
	}

	[Test]
	public async Task Captures_are_written_on_both_the_success_and_the_failure_path()
	{
		_client.Enqueue(Succeeded(200));
		await Execute(Parameters(("url", "https://example.invalid/"), ("captures", Captures(("code", "$status")))));
		Assert.That(_variables.Written["code"], Is.EqualTo(200d));

		_client.Enqueue(Succeeded(404));
		await Execute(Parameters(("url", "https://example.invalid/"), ("captures", Captures(("code", "$status")))));
		Assert.That(_variables.Written["code"], Is.EqualTo(404d));
	}

	[Test]
	public async Task A_transport_timeout_fails_with_the_timeout_code()
	{
		_client.Enqueue(HttpSendOutcome.Failed(HttpFailureKind.Timeout, 10));

		var result = await Execute(Parameters(("url", "https://example.invalid/")));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
		});
	}

	[Test]
	public async Task A_missing_url_fails_without_ever_calling_the_client()
	{
		var result = await Execute(Parameters());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.InvalidParameter));
			Assert.That(_client.Requests, Is.Empty);
		});
	}

	[Test]
	public async Task A_capture_miss_does_not_fail_the_action()
	{
		_client.Enqueue(Succeeded(200, body: "{}"));

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("captures", Captures(("missing", "$.nonexistent")))));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(_variables.Written, Does.Not.ContainKey("missing"));
		});
	}

	[Test]
	public async Task The_resolved_secret_reaches_the_recorded_spec()
	{
		_client.Enqueue(Succeeded(200));

		await Execute(Parameters(("url", "https://example.invalid/"),
			("authType", "bearer"),
			("authSecret", "supersecret-token")));

		var auth = (HttpAuthBearer)_client.Requests.Single().Auth;
		Assert.That(auth.Token, Is.EqualTo("supersecret-token"));
	}

	[Test]
	public async Task A_variable_the_host_refuses_does_not_fail_the_request()
	{
		_client.Enqueue(Succeeded(200));
		_variables.FailWrites = true;

		var result = await Execute(Parameters(("url", "https://example.invalid/"),
			("captures", Captures(("status", "$status")))));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	private Task<ActionResult> Execute(Dictionary<string, object> parameters)
		=> _executor.ExecuteAsync(new ActionExecutionContext { Parameters = parameters });

	private static HttpSendOutcome Succeeded(int statusCode, string body = "{}")
		=> HttpSendOutcome.Succeeded(new HttpResponseSnapshot(statusCode,
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
			body,
			false,
			"application/json",
			5));

	private static Dictionary<string, string> Captures(params (string Name, string Selector)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Selector, StringComparer.Ordinal);

	private static Dictionary<string, object> Parameters(params (string Name, object Value)[] values)
		=> values.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
}
