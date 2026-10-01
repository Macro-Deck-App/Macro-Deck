using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.VideoStreams;
using Mediator;

namespace MacroDeckHost.Tests.UnitTests.VideoStreams;

internal sealed class VideoStreamIntegrations : IIntegrationRegistry
{
	public HashSet<string> Disabled { get; } = new(StringComparer.Ordinal);

	public IReadOnlyList<IIntegration> Integrations => [];

	public event EventHandler<IntegrationAvailabilityChangedEventArgs>? AvailabilityChanged;

	public IActionDefinition? FindAction(string integrationId, string actionId) => null;

	public IActionDefinition? FindAction(QualifiedId id) => null;

	public IReadOnlyList<ActionDescriptor> GetActions(bool enabledOnly = true) => [];

	public bool IsEnabled(string integrationId)
	{
		lock (Disabled)
		{
			return !Disabled.Contains(integrationId);
		}
	}

	public void SetEnabled(string integrationId, bool enabled)
	{
		lock (Disabled)
		{
			if (enabled)
			{
				Disabled.Remove(integrationId);
			}
			else
			{
				Disabled.Add(integrationId);
			}
		}

		AvailabilityChanged?.Invoke(this,
			new IntegrationAvailabilityChangedEventArgs { IntegrationId = integrationId, IsAvailable = enabled });
	}

	public IntegrationOrigin GetOrigin(string integrationId) => IntegrationOrigin.BuiltIn;

	public Task<IntegrationRegistrationResult> RegisterAsync(IIntegration integration,
		IntegrationOrigin origin = IntegrationOrigin.BuiltIn,
		IntegrationMetadata? metadata = null)
		=> throw new NotSupportedException();

	public Task<bool> UnregisterAsync(string integrationId) => Task.FromResult(false);
}

internal sealed class RecordingPublisher : IPublisher
{
	private readonly ConcurrentQueue<object> _published = new();

	public IReadOnlyList<object> Published => [.. _published];

	public IReadOnlyList<T> Of<T>() => [.. _published.OfType<T>()];

	public IReadOnlyList<object> ForSession(string sessionId)
		=>
		[
			.. _published.Where(notification => notification switch
			{
				VideoStreamSessionChangedNotification changed => changed.SessionId == sessionId,
				VideoStreamSessionClosedNotification closed => closed.SessionId == sessionId,
				_ => false
			})
		];

	public ValueTask Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
		where TNotification : INotification
	{
		_published.Enqueue(notification!);
		return ValueTask.CompletedTask;
	}

	public ValueTask Publish(object notification, CancellationToken cancellationToken = default)
	{
		_published.Enqueue(notification);
		return ValueTask.CompletedTask;
	}
}

internal sealed class ScriptedVideoProvider(string id, params string[] streamIds) : IVideoStreamProvider
{
	public string Id { get; } = id;

	public LocalizedText Name { get; set; } = LocalizedText.FromLiteral("Camera " + id);

	public List<VideoStreamDescriptor> Streams { get; } =
		[.. streamIds.Select(streamId => new VideoStreamDescriptor(streamId, LocalizedText.FromLiteral(streamId)))];

	public Func<VideoStreamOpenRequest, CancellationToken, Task<VideoStreamSessionDescription>>? OnOpen { get; set; }

	public ConcurrentQueue<VideoStreamOpenRequest> Opens { get; } = new();

	public ConcurrentQueue<(string SessionId, VideoStreamSessionReason Reason)> Closes { get; } = new();

	public ConcurrentQueue<string> Suspends { get; } = new();

	public ConcurrentQueue<string> Resumes { get; } = new();

	public Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<VideoStreamDescriptor>>([.. Streams]);

	public Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request,
		CancellationToken cancellationToken)
	{
		Opens.Enqueue(request);
		return OnOpen?.Invoke(request, cancellationToken) ??
			Task.FromResult(VideoStreamSessionDescription.Hls("https://camera.local/" + request.StreamId));
	}

	public Task SuspendAsync(string sessionId, CancellationToken cancellationToken)
	{
		Suspends.Enqueue(sessionId);
		return Task.CompletedTask;
	}

	public Task<VideoStreamSessionDescription?> ResumeAsync(string sessionId, CancellationToken cancellationToken)
	{
		Resumes.Enqueue(sessionId);
		return Task.FromResult<VideoStreamSessionDescription?>(
			VideoStreamSessionDescription.Hls("https://camera.local/resumed"));
	}

	public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
	{
		Closes.Enqueue((sessionId, reason));
		return Task.CompletedTask;
	}
}

internal sealed class ScriptedVideoPlugin : IPluginCapabilityInvoker
{
	private int _inFlight;
	private int _maxInFlight;

	public List<VideoStreamProviderDto> Providers { get; } = [];

	public Dictionary<string, List<VideoStreamDescriptorDto>> Streams { get; } = new(StringComparer.Ordinal);

	public ConcurrentQueue<(string Operation, JsonElement Arguments)> Calls { get; } = new();

	public Func<string, JsonElement, Task<object?>>? Handler { get; set; }

	public int MaxInFlight => Volatile.Read(ref _maxInFlight);

	public IReadOnlyList<T> ArgumentsOf<T>(string operation)
		=>
		[
			.. Calls.Where(call => call.Operation == operation)
				.Select(call => call.Arguments.Deserialize<T>(PluginProtocolJson.Options)!)
		];

	public void AddProvider(string id, string registrationId, params string[] streamIds)
	{
		Providers.RemoveAll(provider => provider.Id == id);
		Providers.Add(new VideoStreamProviderDto
		{
			Id = id, Name = LocalizedText.FromLiteral("Camera " + id), RegistrationId = registrationId
		});
		Streams[id] =
		[
			.. streamIds.Select(streamId => new VideoStreamDescriptorDto
			{
				Id = streamId, Name = LocalizedText.FromLiteral(streamId)
			})
		];
	}

	public async Task<JsonElement?> InvokeAsync(string pluginId,
		CapabilityInvokeRequest request,
		CancellationToken cancellationToken)
	{
		Assert.That(request.Kind, Is.EqualTo(CapabilityKinds.VideoStreamProvider));
		var arguments = JsonSerializer.SerializeToElement(request.Arguments, PluginProtocolJson.Options);
		Calls.Enqueue((request.Operation, arguments));

		var now = Interlocked.Increment(ref _inFlight);
		int max;
		while (now > (max = Volatile.Read(ref _maxInFlight)) &&
			Interlocked.CompareExchange(ref _maxInFlight, now, max) != max)
		{
		}

		try
		{
			var result = Handler is null ? Default(request.Operation, arguments) : await Handler(request.Operation, arguments);
			return result is null ? null : JsonSerializer.SerializeToElement(result, PluginProtocolJson.Options);
		}
		finally
		{
			Interlocked.Decrement(ref _inFlight);
		}
	}

	public object? Default(string operation, JsonElement arguments)
		=> operation switch
		{
			CapabilityOperations.VideoStreamProvider.Describe => new VideoStreamProviderDescribePayload
			{
				Providers = [.. Providers]
			},
			CapabilityOperations.VideoStreamProvider.Streams => new VideoStreamProviderStreamsResult
			{
				Streams = Streams.GetValueOrDefault(
					arguments.Deserialize<VideoStreamProviderStreamsArguments>(PluginProtocolJson.Options)!.ProviderId) ?? []
			},
			CapabilityOperations.VideoStreamProvider.SessionOpen => OpenResult(arguments),
			CapabilityOperations.VideoStreamProvider.SessionResume => new VideoStreamSessionResumeResult(),
			_ => null
		};

	public VideoStreamSessionOpenResult OpenResult(JsonElement arguments)
	{
		var open = arguments.Deserialize<VideoStreamSessionOpenArguments>(PluginProtocolJson.Options)!;
		return new VideoStreamSessionOpenResult
		{
			Description = new VideoStreamSessionDescriptionDto { Transport = "hls", Url = "https://camera.local/live" },
			RegistrationId = Providers.Single(provider => provider.Id == open.ProviderId).RegistrationId
		};
	}

	public static RemoteCapabilityException Error(string code)
		=> (RemoteCapabilityException)RemoteCapabilityException.From(new ProtocolError
		{
			Code = code, Message = code, Retryable = true
		});

	public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

	public void AbortAll(string pluginId, ProtocolError reason)
	{
	}

	public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
}

internal sealed class VideoStreamPluginConnection : IPluginConnection
{
	public string ConnectionId { get; } = Guid.NewGuid().ToString();

	public Func<ProtocolEnvelope, Task>? OnSend { get; set; }

	public Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
		=> OnSend?.Invoke(envelope) ?? Task.CompletedTask;

	public Task Close(int closeCode, string reason, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}

internal sealed class VideoStreamWorld : IDisposable
{
	public const string BuiltIn = "integration.cameras";

	public const string PluginId = "com.example.cameras";

	public VideoStreamWorld(Func<PluginSessionRegistry, IPluginCapabilityInvoker>? invoker = null,
		TimeProvider? time = null,
		TimeSpan? inProcessTimeout = null)
	{
		Time = time ?? TimeProvider.System;
		PluginSessions = new PluginSessionRegistry(Time, Serilog.Core.Logger.None);
		Plugin = new ScriptedVideoPlugin();
		Invoker = invoker?.Invoke(PluginSessions) ?? Plugin;
		Registry = new VideoStreamProviderRegistry(Integrations,
			PluginSessions,
			Invoker,
			Publisher,
			Time,
			Serilog.Core.Logger.None,
			inProcessTimeout);
		Broker = new VideoStreamSessionBroker(Registry,
			PluginSessions,
			Publisher,
			Relay,
			Time,
			Serilog.Core.Logger.None,
			TimeSpan.FromMilliseconds(5));
		Sessions = new VideoStreamPluginSessions(PluginSessions, Registry, Broker);
		Sessions.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
	}

	public TimeProvider Time { get; }

	public VideoStreamIntegrations Integrations { get; } = new();

	public RecordingPublisher Publisher { get; } = new();

	public PluginSessionRegistry PluginSessions { get; }

	public ScriptedVideoPlugin Plugin { get; }

	public IPluginCapabilityInvoker Invoker { get; }

	public VideoStreamProviderRegistry Registry { get; }

	public VideoStreamRelay Relay { get; } = new();

	public VideoStreamSessionBroker Broker { get; }

	public VideoStreamPluginSessions Sessions { get; }

	public IntegrationVideoStreamProviderContext Context(string integrationId = BuiltIn)
		=> new(integrationId, Registry, Broker);

	public VideoStreamOpenTicket Open(string providerId, string streamId, string connectionId = "ui-1")
		=> Broker.OpenSession(connectionId, providerId, streamId, ["hls", "mjpeg"]);

	public async Task<(string SessionId, VideoStreamPluginConnection Connection)> ConnectPluginAsync(
		string pluginId = PluginId)
	{
		var sessionId = Guid.CreateVersion7().ToString("D");
		await PluginSessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = "Cameras",
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 3,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = Time.GetUtcNow()
		});
		var connection = new VideoStreamPluginConnection();
		PluginSessions.TryAttach(sessionId, connection, null);
		return (sessionId, connection);
	}

	public async Task<string> AttachPluginAsync(string pluginId = PluginId)
	{
		var (sessionId, connection) = await ConnectPluginAsync(pluginId);
		await Registry.AttachRemoteAsync(pluginId, sessionId, connection);
		return sessionId;
	}

	public void Dispose()
	{
		Sessions.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
		Broker.Dispose();
		Registry.Dispose();
	}

	public static async Task WaitForAsync(Func<bool> condition, string failure)
	{
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail(failure);
	}

	public Task WaitForClosedAsync(string sessionId)
		=> WaitForAsync(() => Publisher.Of<VideoStreamSessionClosedNotification>().Any(closed => closed.SessionId == sessionId),
			$"session {sessionId} never closed");

	public Task WaitForActiveAsync(string sessionId)
		=> WaitForAsync(() => Publisher.Of<VideoStreamSessionChangedNotification>()
				.Any(changed => changed.SessionId == sessionId && changed.State == VideoStreamSessionState.Active),
			$"session {sessionId} never became active");
}
