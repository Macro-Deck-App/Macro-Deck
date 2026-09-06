using System.Net;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Integrations.Delegation.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

[TestFixture]
internal sealed class DelegateClientTests
{
	private static readonly string[] _sceneAndVolume = ["scene", "volume"];
	private static readonly string[] _sceneVolumeAndMuted = ["scene", "volume", "muted"];

	private static readonly Uri BaseUrl = new("http://10.0.0.5:5000");

	[Test]
	public async Task RunScriptAsync_reports_a_successful_run()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new RunScriptResponse
			{
				Success = true,
				ExecutionId = "exec-1",
				Status = ActionExecutionStatus.Succeeded,
				DurationMs = 12
			});

		var result = await client.RunScriptAsync(BaseUrl,
			"token",
			"script-1",
			"client-1",
			1,
			null,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Status, Is.EqualTo("Succeeded"));
		});
	}

	[Test]
	public async Task RunScriptAsync_does_not_throw_on_a_200_carrying_a_transport_error_and_reports_its_message()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new RunScriptResponse
			{
				Success = false,
				Error = new TransportError { Code = "SCRIPT_ERROR", Message = "The script threw an exception." },
				ExecutionId = "exec-1",
				Status = ActionExecutionStatus.Failed,
				DurationMs = 3
			});

		DelegateRunResult result = default!;
		Assert.DoesNotThrowAsync(async () =>
			result = await client.RunScriptAsync(BaseUrl, "token", "script-1", null, 0, null, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo("The script threw an exception."));
		});
	}

	[Test]
	public void RunScriptAsync_maps_the_depth_exceeded_error_code_from_the_nested_error_object()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new RunScriptResponse
			{
				Success = false,
				Error = new TransportError { Code = "SCRIPT_DEPTH_EXCEEDED", Message = "Too deep." },
				ExecutionId = "exec-1",
				Status = ActionExecutionStatus.Failed
			});

		Assert.ThrowsAsync<DelegateDepthExceededException>(async () =>
			await client.RunScriptAsync(BaseUrl, "token", "script-1", null, 10, null, CancellationToken.None));
	}

	[Test]
	public async Task GetScriptsAsync_projects_to_id_and_name()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new GetScriptsResponse
			{
				Scripts =
				[
					new Script { Id = "s1", Name = "Alpha", Flows = "[]" },
					new Script { Id = "s2", Name = "Beta", Flows = "[]" }
				]
			});

		var scripts = await client.GetScriptsAsync(BaseUrl, "token", CancellationToken.None);

		Assert.That(scripts.Select(s => (s.Id, s.Name)),
			Is.EquivalentTo(new[] { ("s1", "Alpha"), ("s2", "Beta") }));
	}

	[Test]
	public async Task LoginAsync_reads_the_access_token()
	{
		using var client = CreateClient(HttpStatusCode.OK, new TokenResponse("token-value", 3600, "admin", "someone"));

		var result = await client.LoginAsync(BaseUrl, "someone", "password", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.AccessToken, Is.EqualTo("token-value"));
			Assert.That(result.ExpiresIn, Is.EqualTo(TimeSpan.FromSeconds(3600)));
		});
	}

	[Test]
	public async Task GetConnectionInfoAsync_reads_the_instance_name()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new GetConnectionInfoResponse { InstanceName = "OTHER-PC", Version = "1.0.0" });

		var info = await client.GetConnectionInfoAsync(BaseUrl, "token", CancellationToken.None);

		Assert.That(info.InstanceName, Is.EqualTo("OTHER-PC"));
	}

	[Test]
	public async Task RunScriptAsync_sends_the_supplied_inputs_in_the_request_body()
	{
		var handler = new StubHandler(HttpStatusCode.OK,
			JsonSerializer.Serialize(new RunScriptResponse
				{
					Success = true,
					ExecutionId = "exec-1",
					Status = ActionExecutionStatus.Succeeded,
					AppliedInputs = ["scene", "volume"]
				},
				DelegateJson.Options));
		using var client = new DelegateClient(new HttpClient(handler));

		var inputs = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["scene"] = "Intermission",
			["volume"] = 70d
		};

		var result = await client.RunScriptAsync(BaseUrl, "token", "script-1", null, 0, inputs, CancellationToken.None);

		using var body = JsonDocument.Parse(handler.LastRequestBody!);
		var sent = body.RootElement.GetProperty("inputs");
		Assert.Multiple(() =>
		{
			Assert.That(handler.LastRequestUri, Does.EndWith("api/scripts/script-1/run"));
			Assert.That(sent.GetProperty("scene").GetString(), Is.EqualTo("Intermission"));
			Assert.That(sent.GetProperty("volume").ValueKind, Is.EqualTo(JsonValueKind.Number));
			Assert.That(sent.GetProperty("volume").GetDouble(), Is.EqualTo(70d));
			Assert.That(result.AppliedInputs, Is.EqualTo(_sceneAndVolume));
		});
	}

	// Acceptance scenario S1.2: the outbound delegate HTTP body carries the caller's values under the
	// agreed "inputs" field, using the bare declared names (not "input:"-prefixed), with each value's own
	// JSON type preserved - a boolean travels as the JSON literal true, not the string "true".
	[Test]
	public async Task RunScriptAsync_sends_every_supplied_input_under_its_bare_name_with_its_own_json_type()
	{
		var handler = new StubHandler(HttpStatusCode.OK,
			JsonSerializer.Serialize(new RunScriptResponse
				{
					Success = true,
					ExecutionId = "exec-1",
					Status = ActionExecutionStatus.Succeeded,
					AppliedInputs = ["scene", "volume", "muted"]
				},
				DelegateJson.Options));
		using var client = new DelegateClient(new HttpClient(handler));

		var inputs = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["scene"] = "BRB",
			["volume"] = 70d,
			["muted"] = true
		};

		await client.RunScriptAsync(BaseUrl, "token", "script-1", null, 1, inputs, CancellationToken.None);

		using var body = JsonDocument.Parse(handler.LastRequestBody!);
		var sent = body.RootElement.GetProperty("inputs");
		Assert.Multiple(() =>
		{
			Assert.That(handler.LastRequestUri, Does.EndWith("api/scripts/script-1/run"));
			Assert.That(sent.EnumerateObject().Select(p => p.Name), Is.EquivalentTo(_sceneVolumeAndMuted));
			Assert.That(sent.GetProperty("scene").ValueKind, Is.EqualTo(JsonValueKind.String));
			Assert.That(sent.GetProperty("scene").GetString(), Is.EqualTo("BRB"));
			Assert.That(sent.GetProperty("volume").ValueKind, Is.EqualTo(JsonValueKind.Number));
			Assert.That(sent.GetProperty("volume").GetDouble(), Is.EqualTo(70d));
			Assert.That(sent.GetProperty("muted").ValueKind, Is.EqualTo(JsonValueKind.True));
		});
	}

	[Test]
	public async Task GetScriptsAsync_carries_the_declared_inputs()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new GetScriptsResponse
			{
				Scripts =
				[
					new Script
					{
						Id = "s1",
						Name = "Alpha",
						Flows = "[]",
						Inputs =
						[
							new ScriptInput
							{
								Name = "volume",
								Type = ScriptInputType.Numeric,
								Required = true,
								DefaultValue = "3"
							}
						]
					}
				]
			});

		var script = (await client.GetScriptsAsync(BaseUrl, "token", CancellationToken.None)).Single();

		var declared = script.Inputs.Single();
		Assert.Multiple(() =>
		{
			Assert.That(declared.Name, Is.EqualTo("volume"));
			Assert.That(declared.Type, Is.EqualTo(MacroDeck.Sdk.Scripts.ScriptInputType.Numeric));
			Assert.That(declared.Required, Is.True);
			Assert.That(declared.DefaultValue, Is.EqualTo("3"));
		});
	}

	[Test]
	public async Task GetScriptsAsync_reports_an_empty_list_for_a_script_without_declarations()
	{
		using var client = CreateClient(HttpStatusCode.OK,
			new GetScriptsResponse { Scripts = [new Script { Id = "s1", Name = "Alpha", Flows = "[]" }] });

		var script = (await client.GetScriptsAsync(BaseUrl, "token", CancellationToken.None)).Single();

		Assert.That(script.Inputs, Is.Empty);
	}

	private static DelegateClient CreateClient<T>(HttpStatusCode statusCode, T body)
	{
		var json = JsonSerializer.Serialize(body, DelegateJson.Options);
		return new DelegateClient(new HttpClient(new StubHandler(statusCode, json)));
	}

	private sealed class StubHandler : HttpMessageHandler
	{
		private readonly HttpStatusCode _statusCode;
		private readonly string _json;

		public StubHandler(HttpStatusCode statusCode, string json)
		{
			_statusCode = statusCode;
			_json = json;
		}

		public string? LastRequestBody { get; private set; }

		public string? LastRequestUri { get; private set; }

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			LastRequestUri = request.RequestUri?.ToString();
			if (request.Content is not null)
			{
				LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);
			}

			return new HttpResponseMessage(_statusCode)
			{
				Content = new StringContent(_json, Encoding.UTF8, "application/json")
			};
		}
	}
}
