using System.Text.Json;
using MacroDeckHost.Integrations.Http.Actions;
using MacroDeckHost.Integrations.Http.Client;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Http;

[TestFixture]
internal sealed class HttpResponseCaptureTests
{
	private RecordingVariableApi _variables = null!;
	private HttpVariableAccessor _accessor = null!;

	[SetUp]
	public void SetUp()
	{
		_variables = new RecordingVariableApi();
		_accessor = new HttpVariableAccessor { Current = _variables };
	}

	[Test]
	public async Task Every_reserved_token_resolves_to_its_documented_type_and_value()
	{
		var response = Snapshot(statusCode: 201, body: "{}", durationMs: 123, truncated: true);
		var captures = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["status"] = "$status",
			["ok"] = "$ok",
			["dur"] = "$duration",
			["body"] = "$body",
			["trunc"] = "$truncated"
		};

		await HttpResponseCapture.ApplyAsync(_accessor, captures, response, statusExpected: true, SilentLogger());

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Written["status"], Is.EqualTo(201d));
			Assert.That(_variables.Written["ok"], Is.EqualTo(true));
			Assert.That(_variables.Written["dur"], Is.EqualTo(123d));
			Assert.That(_variables.Written["body"], Is.EqualTo("{}"));
			Assert.That(_variables.Written["trunc"], Is.EqualTo(true));
		});
	}

	[Test]
	public async Task Headers_token_serializes_every_header()
	{
		var response = Snapshot(headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Content-Type"] = "application/json",
			["X-Rate-Limit"] = "60"
		});

		await HttpResponseCapture.ApplyAsync(_accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["h"] = "$headers" },
			response,
			statusExpected: true,
			SilentLogger());

		var expected = JsonSerializer.Serialize(response.Headers);
		Assert.That(_variables.Written["h"], Is.EqualTo(expected));
	}

	[Test]
	public async Task A_single_header_lookup_is_case_insensitive()
	{
		var response = Snapshot(headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Content-Type"] = "application/json"
		});

		await HttpResponseCapture.ApplyAsync(_accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["ct"] = "$header:content-type" },
			response,
			statusExpected: true,
			SilentLogger());

		Assert.That(_variables.Written["ct"], Is.EqualTo("application/json"));
	}

	[Test]
	public async Task An_absent_header_is_a_miss_and_does_not_fall_through_to_a_json_path()
	{
		var response = Snapshot(headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
			body: """{"x-missing":"should-never-be-used"}""");

		await HttpResponseCapture.ApplyAsync(_accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = "$header:x-missing" },
			response,
			statusExpected: true,
			SilentLogger());

		Assert.That(_variables.Written, Does.Not.ContainKey("v"));
	}

	[TestCase("num", 7d)]
	[TestCase("flag", true)]
	[TestCase("str", "hi")]
	public async Task A_json_path_selector_resolves_every_value_kind_through_to_the_variable(
		string path,
		object expectedValue)
	{
		var response = Snapshot(body: """{"num":7,"flag":true,"str":"hi"}""");

		await HttpResponseCapture.ApplyAsync(_accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = path },
			response,
			statusExpected: true,
			SilentLogger());

		Assert.That(_variables.Written["v"], Is.EqualTo(expectedValue));
	}

	[Test]
	public async Task A_json_path_miss_writes_nothing()
	{
		var response = Snapshot(body: "{}");

		await HttpResponseCapture.ApplyAsync(_accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["v"] = "$.nonexistent" },
			response,
			statusExpected: true,
			SilentLogger());

		Assert.That(_variables.Written, Does.Not.ContainKey("v"));
	}

	[Test]
	public async Task A_non_json_body_still_writes_the_reserved_tokens()
	{
		var response = Snapshot(body: "not json at all", statusCode: 200);

		await HttpResponseCapture.ApplyAsync(_accessor,
			new Dictionary<string, string>(StringComparer.Ordinal) { ["status"] = "$status", ["body"] = "$body" },
			response,
			statusExpected: true,
			SilentLogger());

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Written["status"], Is.EqualTo(200d));
			Assert.That(_variables.Written["body"], Is.EqualTo("not json at all"));
		});
	}

	[Test]
	public async Task A_transport_failure_writes_only_the_four_always_available_tokens_and_skips_json_paths()
	{
		var captures = new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["status"] = "$status",
			["ok"] = "$ok",
			["dur"] = "$duration",
			["body"] = "$body",
			["path"] = "$.name"
		};

		await HttpResponseCapture.ApplyTransportFailureAsync(_accessor, captures, durationMs: 55, SilentLogger());

		Assert.Multiple(() =>
		{
			Assert.That(_variables.Written["status"], Is.EqualTo(0d));
			Assert.That(_variables.Written["ok"], Is.EqualTo(false));
			Assert.That(_variables.Written["dur"], Is.EqualTo(55d));
			Assert.That(_variables.Written["body"], Is.EqualTo(string.Empty));
			Assert.That(_variables.Written, Does.Not.ContainKey("path"));
		});
	}

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private static HttpResponseSnapshot Snapshot(
		int statusCode = 200,
		IReadOnlyDictionary<string, string>? headers = null,
		string body = "{}",
		bool truncated = false,
		long durationMs = 10)
		=> new(statusCode,
			headers ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
			body,
			truncated,
			"application/json",
			durationMs);
}
