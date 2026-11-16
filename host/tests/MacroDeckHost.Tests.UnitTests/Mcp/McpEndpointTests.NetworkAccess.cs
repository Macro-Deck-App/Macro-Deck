using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeckHost.Application.Configuration;
using MacroDeckHost.Tests.UnitTests.Auth;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Mcp;

public partial class McpEndpointTests
{
	private const string UnknownPageId = "0123456789abcdef0123456789abcdef";

	[Test]
	public async Task Only_clients_on_this_computer_connect_while_network_access_is_off()
	{
		var token = await CreateToken();

		var fromNetwork = await PostInitialize(token.Plaintext, FakeConnectionShape.PublicLan);
		var fromThisComputer = await PostInitialize(token.Plaintext, FakeConnectionShape.LoopbackOnPublicPort);
		var throughOwnAddress = await PostInitialize(token.Plaintext,
			FakeConnectionShape.LoopbackOnPublicPort,
			host: "192.168.1.20:8191");
		var desktopListener = await PostInitialize(token.Plaintext);
		var endpoint = await Send(HttpMethod.Get, "/api/mcp/endpoint", FakeConnectionShape.Loopback);
		var allowed = (await endpoint.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("networkClientsAllowed")
			.GetBoolean();

		await Assert.MultipleAsync(() =>
		{
			Assert.That(fromNetwork.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(fromThisComputer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(throughOwnAddress.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(desktopListener.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(allowed, Is.False);
			return Task.CompletedTask;
		});
	}

	[TestCase(false)]
	[TestCase(true)]
	public async Task A_request_passed_on_by_a_reverse_proxy_is_refused_while_network_access_is_off(bool proxyTrusted)
	{
		var token = await CreateToken();
		var previous = ReverseProxyTrust.Enabled;
		ReverseProxyTrust.Enabled = proxyTrusted;
		HttpResponseMessage transport;
		HttpResponseMessage secretPage;
		HttpResponseMessage confirmation;
		try
		{
			transport = await SendProxied(HttpMethod.Post, "/mcp", token.Plaintext);
			secretPage = await SendProxied(HttpMethod.Get, $"/mcp/secret-entry/{UnknownPageId}", null);
			confirmation = await SendProxied(HttpMethod.Post, $"/mcp/confirm/{UnknownPageId}", null);
		}
		finally
		{
			ReverseProxyTrust.Enabled = previous;
		}

		await Assert.MultipleAsync(() =>
		{
			Assert.That(transport.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(secretPage.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(confirmation.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Turning_network_access_on_and_off_takes_effect_without_a_restart()
	{
		var token = await CreateToken();

		HttpResponseMessage whileOn;
		bool reportedOn;
		try
		{
			var turnedOn = await SetNetworkAccess(true);
			reportedOn = turnedOn.GetProperty("allowed").GetBoolean();
			whileOn = await PostInitialize(token.Plaintext, FakeConnectionShape.PublicLan);
		}
		finally
		{
			await SetNetworkAccess(false);
		}

		var whileOff = await PostInitialize(token.Plaintext, FakeConnectionShape.PublicLan);

		await Assert.MultipleAsync(() =>
		{
			Assert.That(reportedOn, Is.True);
			Assert.That(whileOn.StatusCode, Is.EqualTo(HttpStatusCode.OK));
			Assert.That(whileOff.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task Turning_network_access_off_cuts_off_network_clients_and_keeps_local_ones()
	{
		var token = await CreateToken();
		await SetNetworkAccess(true);
		McpClientPair remote;
		try
		{
			remote = new McpClientPair(
				await Connect(token.Plaintext, LegacyProtocol, shape: FakeConnectionShape.PublicLan),
				await Connect(token.Plaintext, null, shape: FakeConnectionShape.PublicLan));
		}
		catch
		{
			await SetNetworkAccess(false);
			throw;
		}

		await using var remoteSession = remote.Session;
		await using var remoteStateless = remote.Stateless;
		await using var localSession = await Connect(token.Plaintext, LegacyProtocol);
		await using var localStateless = await Connect(token.Plaintext, null);
		await remoteSession.ListToolsAsync();
		await remoteStateless.ListToolsAsync();
		await localStateless.ListToolsAsync();
		var before = await Connections(token.Id);

		await SetNetworkAccess(false);
		var after = await Connections(token.Id);

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(before, Has.Count.EqualTo(4));
			Assert.That(after, Has.Count.EqualTo(2));
			Assert.That(async () => await remoteSession.ListToolsAsync(), Throws.Exception);
			Assert.That(async () => await remoteStateless.ListToolsAsync(), Throws.Exception);
			Assert.That(await localSession.ListToolsAsync(), Is.Not.Empty);
			Assert.That(await localStateless.ListToolsAsync(), Is.Not.Empty);
		});
	}

	[Test]
	public async Task Turning_network_access_off_aborts_a_call_a_network_client_is_waiting_on()
	{
		var token = await CreateToken();
		await SetNetworkAccess(true);
		try
		{
			await using var client = await Connect(token.Plaintext,
				LegacyProtocol,
				async (_, _) =>
				{
					await SetNetworkAccess(false);
					return new ElicitResult { Action = "decline" };
				},
				shape: FakeConnectionShape.PublicLan);

			Assert.That(async () => await client.CallToolAsync("set_secret",
					new Dictionary<string, object?> { ["kind"] = "secret", ["label"] = "API key" }),
				Throws.Exception);
			Assert.That(await Connections(token.Id), Is.Empty);
		}
		finally
		{
			await SetNetworkAccess(false);
		}
	}

	private async Task<JsonElement> SetNetworkAccess(bool allowed)
	{
		var response = await Send(HttpMethod.Put,
			"/api/mcp/network-access",
			FakeConnectionShape.Loopback,
			body: new { allowed });
		Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
		return await response.Content.ReadFromJsonAsync<JsonElement>();
	}

	private async Task<HttpResponseMessage> SendProxied(HttpMethod method, string path, string? token)
	{
		using var request = new HttpRequestMessage(method, path);
		request.Headers.Add(FakeConnectionStartupFilter.ShapeHeader, nameof(FakeConnectionShape.LoopbackOnPublicPort));
		request.Headers.Add("X-Forwarded-For", "203.0.113.7");
		request.Headers.Accept.ParseAdd("application/json");
		request.Headers.Accept.ParseAdd("text/event-stream");
		if (token is not null)
		{
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
			request.Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/list" });
			request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
		}

		return await _client.SendAsync(request);
	}

	private sealed record McpClientPair(McpClient Session, McpClient Stateless);
}
