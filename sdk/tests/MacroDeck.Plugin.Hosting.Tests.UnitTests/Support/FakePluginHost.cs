using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>
/// A host that speaks the protocol, on a real loopback port.
///
/// <para>
/// It has to be a real Kestrel rather than <c>TestServer</c>: the plugin connects with a
/// <see cref="ClientWebSocket" />, which cannot be pointed at an in-memory test server, and the two
/// things most worth testing about the upgrade - the subprotocol negotiation and the
/// <c>Authorization</c> header - only exist on a real one.
/// </para>
/// </summary>
internal sealed class FakePluginHost : IAsyncDisposable
{
	private readonly WebApplication _application;
	private readonly Channel<ProtocolEnvelope> _received = Channel.CreateUnbounded<ProtocolEnvelope>();

	private readonly TaskCompletionSource<WebSocket> _connected
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly TaskCompletionSource _welcomed = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly ConcurrentQueue<string> _types = new();
	private readonly SemaphoreSlim _sending = new(1, 1);
	private WebSocket? _current;
	private int _connections;
	private int _pairingSequence;
	private readonly ConcurrentDictionary<string, PairingState> _pairingRequests = new(StringComparer.Ordinal);

	private FakePluginHost(WebApplication application) => _application = application;

	/// <summary>One request the host received, at the HTTP level - every endpoint, not only pairing, so
	/// a test can assert on what was and was not sent.</summary>
	public sealed record RecordedRequest(
		string Method,
		string Path,
		string QueryString,
		IReadOnlyDictionary<string, string> Headers);

	private sealed class PairingState
	{
		public required string PluginId;
		public required string CodeChallenge;
		public int PollCount;
		public bool Redeemed;
	}

	/// <summary>Every request the host received, in order, regardless of which endpoint handled it.</summary>
	public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

	/// <summary>
	/// Whether the protocol descriptor advertises pairing support. Off simulates a host that predates
	/// pairing entirely.
	/// </summary>
	public bool PairingDescriptorAbsent { get; set; }

	/// <summary>Whether <c>POST /api/plugins/pairing</c> answers 404, simulating a host whose descriptor
	/// claims support that the endpoint itself does not have.</summary>
	public bool PairingCreateNotFound { get; set; }

	/// <summary>What the descriptor reports for Developer Mode. Null is a host too old to report it.</summary>
	public bool? PairingDeveloperModeEnabled { get; set; }

	/// <summary>Whether <c>POST /api/plugins/pairing</c> answers 403, which is what the real host does
	/// while Developer Mode is off.</summary>
	public bool PairingCreateForbidden { get; set; }

	/// <summary>
	/// Set to auto-approve a pairing request once it has been polled this many times; left null, a
	/// pairing request stays pending forever (unless <see cref="PairingRejected" /> is set).
	/// </summary>
	public int? PairingApproveAfterPolls { get; set; }

	/// <summary>Every status poll answers "rejected" instead of "pending" once set.</summary>
	public bool PairingRejected { get; set; }

	/// <summary>Advertised and used as the actual pairing request lifetime.</summary>
	public int PairingRequestLifetimeSeconds { get; set; } = 60;

	/// <summary>Advertised as the poll interval a plugin should use.</summary>
	public int PairingPollIntervalSeconds { get; set; } = 1;

	/// <summary>Pairing requests created, in order.</summary>
	public ConcurrentQueue<PluginPairingRequest> PairingCreates { get; } = new();

	/// <summary>Request ids polled for status, in order - may repeat.</summary>
	public ConcurrentQueue<string> PairingStatusPolls { get; } = new();

	/// <summary>Redemption attempts, in order.</summary>
	public ConcurrentQueue<PluginPairingRedemptionRequest> PairingRedemptions { get; } = new();

	/// <summary>The url the plugin should be pointed at.</summary>
	public string Url { get; private set; } = string.Empty;

	/// <summary>Registration requests the plugin made.</summary>
	public ConcurrentQueue<PluginRegistrationRequest> Registrations { get; } = new();

	/// <summary>Session requests the plugin made, in order.</summary>
	public ConcurrentQueue<PluginSessionRequest> Sessions { get; } = new();

	/// <summary>Sessions the plugin ended explicitly.</summary>
	public ConcurrentQueue<string> DeletedSessions { get; } = new();

	/// <summary>The enrollment tokens presented, so a test can prove one was used exactly once.</summary>
	public ConcurrentQueue<string?> EnrollmentTokens { get; } = new();

	/// <summary>The message types the plugin has sent, in order.</summary>
	public IReadOnlyCollection<string> Types => _types;

	/// <summary>The subprotocols the plugin offered on the upgrade.</summary>
	public IReadOnlyList<string> OfferedSubProtocols { get; private set; } = [];

	/// <summary>The Authorization header the plugin presented on the upgrade.</summary>
	public string? UpgradeAuthorization { get; private set; }

	/// <summary>Whether the plugin ever sent its secret on the upgrade, which it must never do.</summary>
	public bool SecretSentOnUpgrade { get; private set; }

	/// <summary>Everything the plugin sent over the socket.</summary>
	public ChannelReader<ProtocolEnvelope> Received => _received.Reader;

	/// <summary>How many sockets the plugin has opened, so a reconnect test can tell them apart.</summary>
	public int Connections => Volatile.Read(ref _connections);

	/// <summary>
	/// When set, every socket is accepted and then aborted instead of being welcomed: a host that
	/// answers the upgrade but never completes the handshake, which is not a session having opened.
	/// </summary>
	public bool RefuseHandshake { get; set; }

	/// <summary>
	/// Kills the socket the plugin is currently on, the way a network blip does: aborted rather than
	/// closed, so the plugin sees a dropped connection and not a goodbye it should stop resuming after.
	/// </summary>
	public void DropCurrentConnection() => Volatile.Read(ref _current)?.Abort();

	public static async Task<FakePluginHost> StartAsync()
	{
		var builder = WebApplication.CreateBuilder();
		builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
		builder.Logging.ClearProviders();

		var application = builder.Build();
		var host = new FakePluginHost(application);

		application.UseWebSockets();
		host.MapEndpoints(application);

		await application.StartAsync();

		host.Url = application.Services.GetRequiredService<IServer>()
			.Features.Get<IServerAddressesFeature>()!
			.Addresses.First();

		return host;
	}

	/// <summary>The socket the plugin connected on, once it has.</summary>
	public Task<WebSocket> ConnectedAsync() => _connected.Task;

	/// <summary>
	/// Completes once the host has finished answering <c>session.hello</c>. Waiting for the hello
	/// itself is not enough: the welcome is sent after it is published, so a test that sends
	/// immediately would race the welcome on the same socket.
	/// </summary>
	public Task WelcomedAsync() => _welcomed.Task.WaitAsync(TimeSpan.FromSeconds(10));

	/// <summary>
	/// Sends one envelope to the plugin. Serialized, because a WebSocket permits one outstanding send
	/// and the host's own welcome goes out on the same socket.
	/// </summary>
	public async Task SendAsync(WebSocket socket, ProtocolEnvelope envelope)
	{
		await _sending.WaitAsync();

		try
		{
			await socket.SendAsync(ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope),
				WebSocketMessageType.Text,
				endOfMessage: true,
				CancellationToken.None);
		}
		finally
		{
			_sending.Release();
		}
	}

	/// <summary>Waits for the next message of a given type, failing rather than hanging.</summary>
	public async Task<ProtocolEnvelope> NextAsync(string type, TimeSpan? timeout = null)
	{
		using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(10));

		await foreach (var envelope in _received.Reader.ReadAllAsync(deadline.Token))
		{
			if (string.Equals(envelope.Type, type, StringComparison.Ordinal))
			{
				return envelope;
			}
		}

		throw new InvalidOperationException($"The plugin never sent a '{type}'.");
	}

	public async ValueTask DisposeAsync()
	{
		await _application.StopAsync();
		await _application.DisposeAsync();
		_sending.Dispose();
	}

	private void MapEndpoints(WebApplication application)
	{
		application.Use(async (context, next) =>
		{
			var headers = context.Request.Headers.ToDictionary(header => header.Key,
				header => header.Value.ToString(),
				StringComparer.OrdinalIgnoreCase);

			Requests.Enqueue(new RecordedRequest(context.Request.Method,
				context.Request.Path.Value ?? string.Empty,
				context.Request.QueryString.Value ?? string.Empty,
				headers));

			await next(context);
		});

		application.MapGet(ProtocolConstants.ProtocolDiscoveryPath,
			() => Results.Json(new PluginProtocolDescriptor
				{
					SupportedVersions = ProtocolVersions.Supported,
					CapabilityKinds = CapabilityKinds.All,
					Limits = TestSession.Limits(),
					Timeouts = TestSession.Timeouts(),
					Pairing = PairingDescriptorAbsent
						? null
						: new PluginPairingDescriptor
						{
							Supported = true,
							RequestLifetimeSeconds = PairingRequestLifetimeSeconds,
							PollIntervalSeconds = PairingPollIntervalSeconds,
							DeveloperModeEnabled = PairingDeveloperModeEnabled
						}
				},
				PluginProtocolJson.Options));

		application.MapPost(ProtocolConstants.PairingPath,
			async (HttpContext context) =>
			{
				if (PairingCreateNotFound)
				{
					return Results.NotFound();
				}

				if (PairingCreateForbidden)
				{
					return Results.StatusCode(StatusCodes.Status403Forbidden);
				}

				var request
					= await context.Request.ReadFromJsonAsync<PluginPairingRequest>(PluginProtocolJson.Options);
				PairingCreates.Enqueue(request!);

				var requestId = $"pairing-{Interlocked.Increment(ref _pairingSequence)}";
				_pairingRequests[requestId] = new PairingState
				{
					PluginId = request!.PluginId,
					CodeChallenge = request.CodeChallenge
				};

				return Results.Json(new PluginPairingResponse
					{
						RequestId = requestId,
						ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PairingRequestLifetimeSeconds),
						PollIntervalSeconds = PairingPollIntervalSeconds
					},
					PluginProtocolJson.Options,
					statusCode: StatusCodes.Status201Created);
			});

		application.MapGet($"{ProtocolConstants.PairingPath}/{{requestId}}",
			(string requestId) =>
			{
				PairingStatusPolls.Enqueue(requestId);

				if (!_pairingRequests.TryGetValue(requestId, out var pairing))
				{
					// Deliberately 200 with status "expired", exactly like the real host: an unknown or
					// pruned id keeps the client's state machine total instead of adding an error path.
					return Results.Json(new PluginPairingStatusResponse
							{ Status = PluginPairingStatuses.Expired, ExpiresAt = DateTimeOffset.UtcNow },
						PluginProtocolJson.Options);
				}

				var polls = Interlocked.Increment(ref pairing.PollCount);

				var status = PairingRejected
					? PluginPairingStatuses.Rejected
					: PairingApproveAfterPolls is { } threshold && polls >= threshold
						? PluginPairingStatuses.Approved
						: PluginPairingStatuses.Pending;

				return Results.Json(new PluginPairingStatusResponse
					{
						Status = status,
						ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(PairingRequestLifetimeSeconds)
					},
					PluginProtocolJson.Options);
			});

		application.MapPost($"{ProtocolConstants.PairingPath}/{{requestId}}/redemption",
			async (HttpContext context, string requestId) =>
			{
				var request = await context.Request.ReadFromJsonAsync<PluginPairingRedemptionRequest>(
					PluginProtocolJson.Options);
				PairingRedemptions.Enqueue(request!);

				if (!_pairingRequests.TryGetValue(requestId, out var pairing) || pairing.Redeemed)
				{
					return Results.Unauthorized();
				}

				var approved = !PairingRejected &&
					PairingApproveAfterPolls is { } threshold &&
					Volatile.Read(ref pairing.PollCount) >= threshold;

				if (!approved)
				{
					return Results.Unauthorized();
				}

				var expectedChallenge
					= Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(request!.CodeVerifier)));

				if (!string.Equals(expectedChallenge, pairing.CodeChallenge, StringComparison.Ordinal))
				{
					return Results.Unauthorized();
				}

				pairing.Redeemed = true;

				return Results.Json(new PluginRegistrationResponse
					{
						PluginId = pairing.PluginId,
						PluginSecret = new string('p', PluginAuthDefaults.MinPluginSecretLength)
					},
					PluginProtocolJson.Options,
					statusCode: StatusCodes.Status201Created);
			});

		application.MapPost(ProtocolConstants.RegistrationPath,
			async (HttpContext context) =>
			{
				EnrollmentTokens.Enqueue(context.Request.Headers[PluginAuthDefaults.EnrollmentTokenHeaderName]
					.FirstOrDefault());

				var request
					= await context.Request.ReadFromJsonAsync<PluginRegistrationRequest>(PluginProtocolJson.Options);
				Registrations.Enqueue(request!);

				return Results.Json(new PluginRegistrationResponse
					{
						PluginId = request!.PluginId,
						PluginSecret = new string('s', PluginAuthDefaults.MinPluginSecretLength)
					},
					PluginProtocolJson.Options,
					statusCode: StatusCodes.Status201Created);
			});

		application.MapPost(ProtocolConstants.SessionsPath,
			async (HttpContext context) =>
			{
				if (string.IsNullOrEmpty(
						context.Request.Headers[PluginAuthDefaults.PluginIdHeaderName].FirstOrDefault()) ||
					string.IsNullOrEmpty(context.Request.Headers[PluginAuthDefaults.PluginSecretHeaderName]
						.FirstOrDefault()))
				{
					return Results.Unauthorized();
				}

				var request = await context.Request.ReadFromJsonAsync<PluginSessionRequest>(PluginProtocolJson.Options);
				Sessions.Enqueue(request!);

				return Results.Json(new PluginSessionResponse
					{
						SessionId = $"session-{Sessions.Count}",
						SessionToken = "session-token",
						NegotiatedVersion = ProtocolVersions.Current,
						Capabilities =
						[
							.. request!.Capabilities
								.Select(capability => capability.Kind)
								.Distinct(StringComparer.Ordinal)
								.Select(kind => CapabilityNegotiationResult.Accept(kind, 1))
						],
						Limits = TestSession.Limits(),
						Timeouts = TestSession.Timeouts()
					},
					PluginProtocolJson.Options,
					statusCode: StatusCodes.Status201Created);
			});

		application.MapDelete($"{ProtocolConstants.SessionsPath}/{{sessionId}}",
			(string sessionId) =>
			{
				DeletedSessions.Enqueue(sessionId);
				return Results.NoContent();
			});

		application.Map(ProtocolConstants.WebSocketPath,
			async (HttpContext context) =>
			{
				if (!context.WebSockets.IsWebSocketRequest)
				{
					return Results.BadRequest();
				}

				OfferedSubProtocols = [.. context.WebSockets.WebSocketRequestedProtocols];
				UpgradeAuthorization = context.Request.Headers.Authorization.FirstOrDefault();
				SecretSentOnUpgrade = context.Request.Headers.ContainsKey(PluginAuthDefaults.PluginSecretHeaderName);

				using var socket
					= await context.WebSockets.AcceptWebSocketAsync(ProtocolConstants.WebSocketSubProtocol);

				// Published before the count rises, so a test that waits on the count and then drops is
				// certain to be dropping the socket it just waited for.
				Volatile.Write(ref _current, socket);
				Interlocked.Increment(ref _connections);
				_connected.TrySetResult(socket);

				if (RefuseHandshake)
				{
					socket.Abort();
					return Results.Empty;
				}

				await ReadAsync(socket);
				return Results.Empty;
			});
	}

	private static string Base64Url(byte[] bytes)
		=> Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

	private async Task ReadAsync(WebSocket socket)
	{
		var buffer = new byte[64 * 1024];

		while (socket.State == WebSocketState.Open)
		{
			WebSocketReceiveResult received;

			try
			{
				received = await socket.ReceiveAsync(buffer, CancellationToken.None);
			}
			catch (WebSocketException)
			{
				return;
			}

			if (received.MessageType == WebSocketMessageType.Close)
			{
				return;
			}

			var read = ProtocolEnvelopeReader.Read(buffer.AsSpan(0, received.Count));

			if (read.Envelope is { } envelope)
			{
				_types.Enqueue(envelope.Type);
				_received.Writer.TryWrite(envelope);

				// The host's half of the handshake, so a plugin under test gets past it without every
				// test spelling it out.
				if (string.Equals(envelope.Type, MessageTypes.SessionHello, StringComparison.Ordinal))
				{
					var hello = envelope.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);

					await SendAsync(socket,
						new ProtocolEnvelope
						{
							Type = MessageTypes.SessionWelcome,
							Id = Guid.CreateVersion7().ToString(),
							CorrelationId = envelope.Id,
							Payload = JsonSerializer.SerializeToElement(new SessionWelcomePayload
								{
									SessionId = hello?.SessionId ?? "session-1",
									Resumed = !string.IsNullOrEmpty(hello?.ResumeSessionId)
								},
								PluginProtocolJson.Options)
						});

					_welcomed.TrySetResult();
				}
			}
		}
	}
}
