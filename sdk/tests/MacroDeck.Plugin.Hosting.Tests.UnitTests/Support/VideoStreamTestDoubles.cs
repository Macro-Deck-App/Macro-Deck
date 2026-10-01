using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.VideoStreams;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

internal sealed class TestVideoProvider(string id = "cam") : IVideoStreamProvider
{
	private readonly ConcurrentQueue<(string SessionId, VideoStreamSessionReason Reason)> _closes = new();

	public string Id { get; } = id;

	public LocalizedText Name { get; } = LocalizedText.FromLiteral("Camera");

	public IReadOnlyList<VideoStreamDescriptor> Streams { get; set; } =
		[new VideoStreamDescriptor("main", LocalizedText.FromLiteral("Main"))];

	public Func<VideoStreamOpenRequest, CancellationToken, Task<VideoStreamSessionDescription>> Open { get; set; } =
		(_, _) => Task.FromResult(VideoStreamSessionDescription.Hls("http://camera.local/main.m3u8"));

	public Func<string, VideoStreamSessionReason, Task> OnClose { get; set; } = (_, _) => Task.CompletedTask;

	private int _openCount;

	public int OpenCount => Volatile.Read(ref _openCount);

	public IReadOnlyList<(string SessionId, VideoStreamSessionReason Reason)> Closes => [.. _closes];

	public Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken)
		=> Task.FromResult(Streams);

	public Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request,
		CancellationToken cancellationToken)
	{
		Interlocked.Increment(ref _openCount);
		return Open(request, cancellationToken);
	}

	public Func<string, Task<VideoStreamSessionDescription?>> Resume { get; set; } =
		_ => Task.FromResult<VideoStreamSessionDescription?>(null);

	public Task<VideoStreamSessionDescription?> ResumeAsync(string sessionId, CancellationToken cancellationToken)
		=> Resume(sessionId);

	public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
	{
		_closes.Enqueue((sessionId, reason));
		return OnClose(sessionId, reason);
	}
}

internal sealed class TestVideoIntegration : IPluginIntegration, IVideoStreamIntegration
{
	public IReadOnlyList<IActionDefinition> Actions => [];

	public IVideoStreamProviderContext? Context { get; private set; }

	public Func<IVideoStreamProviderContext, Task> OnVideoInitialize { get; set; } = _ => Task.CompletedTask;

	public bool IsInitialized { get; private set; }

	public bool WasInitializedBeforeVideo { get; private set; }

	public int ShutdownCount { get; private set; }

	public Task InitializeAsync(IIntegrationContext context)
	{
		IsInitialized = true;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		ShutdownCount++;
		IsInitialized = false;
		return Task.CompletedTask;
	}

	public Task InitializeAsync(IVideoStreamProviderContext context, CancellationToken cancellationToken = default)
	{
		Context = context;
		WasInitializedBeforeVideo = IsInitialized;
		return OnVideoInitialize(context);
	}
}

internal sealed record HostCall(string Api, string Operation, object? Arguments);

internal sealed class VideoRecordingInvoker : IHostInvoker
{
	private readonly ConcurrentQueue<HostCall> _calls = new();

	public Func<HostCall, Exception?> Fail { get; set; } = _ => null;

	public IReadOnlyList<HostCall> Calls => [.. _calls];

	public bool Hang { get; set; }

	public Task<JsonElement?> InvokeAsync(string api, string operation, object? arguments,
		CancellationToken cancellationToken)
	{
		var call = new HostCall(api, operation, arguments);
		_calls.Enqueue(call);
		if (Hang)
		{
			return new TaskCompletionSource<JsonElement?>().Task;
		}

		return Fail(call) is { } exception
			? Task.FromException<JsonElement?>(exception)
			: Task.FromResult<JsonElement?>(null);
	}

	public bool TryComplete(ProtocolEnvelope result) => false;

	public static Exception Unsupported(HostCall call)
		=> HostInvocationException.CreateNonRetryable(ProtocolErrorCodes.CapabilityUnsupported,
			$"'{call.Api}' is not a host api this host knows.");
}

internal sealed class VideoStreamFixture : IDisposable
{
	private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();

	public VideoStreamFixture(IPluginIntegration? integration = null, TimeProvider? time = null)
	{
		Registry = new VideoStreamProviderRegistry(Invoker,
			State,
			TestMetadata.Default,
			time ?? TimeProvider.System,
			Serilog.Core.Logger.None);
		Handler = new VideoStreamProviderCapabilityHandler([integration ?? new TestVideoIntegration()],
			Registry,
			Serilog.Core.Logger.None);
		Context = Registry.ContextFor(null);
	}

	public PluginConnectionState State { get; } = new();

	public VideoRecordingInvoker Invoker { get; } = new();

	public VideoStreamProviderRegistry Registry { get; }

	public VideoStreamProviderCapabilityHandler Handler { get; }

	public IVideoStreamProviderContext Context { get; }

	public void Dispose()
	{
		Registry.Dispose();
		_services.Dispose();
	}

	public Task<CapabilityInvocationResult> InvokeAsync(string operation, object? arguments = null,
		CancellationToken cancellationToken = default)
		=> Handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = CapabilityKinds.VideoStreamProvider,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = operation,
				Arguments = arguments is null
					? null
					: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
				CorrelationId = Guid.NewGuid().ToString(),
				Services = _services
			},
			cancellationToken);

	public Task<CapabilityInvocationResult> OpenAsync(string sessionId, string providerId = "cam",
		params string[] transports)
		=> InvokeAsync(CapabilityOperations.VideoStreamProvider.SessionOpen,
			new Protocol.Capabilities.VideoStreamProvider.VideoStreamSessionOpenArguments
			{
				SessionId = sessionId,
				ProviderId = providerId,
				StreamId = "main",
				AcceptedTransports = transports.Length == 0 ? ["hls"] : transports
			});

	public Task<CapabilityInvocationResult> CloseAsync(string sessionId, string providerId = "cam",
		string reason = "ConsumerClosed")
		=> InvokeAsync(CapabilityOperations.VideoStreamProvider.SessionClose,
			new Protocol.Capabilities.VideoStreamProvider.VideoStreamSessionCloseArguments
			{
				SessionId = sessionId, ProviderId = providerId, Reason = reason
			});

	public Task<CapabilityInvocationResult> ResumeAsync(string sessionId, string providerId = "cam")
		=> InvokeAsync(CapabilityOperations.VideoStreamProvider.SessionResume,
			new Protocol.Capabilities.VideoStreamProvider.VideoStreamSessionArguments
			{
				SessionId = sessionId, ProviderId = providerId
			});

	public Task<CapabilityInvocationResult> SuspendAsync(string sessionId, string providerId = "cam")
		=> InvokeAsync(CapabilityOperations.VideoStreamProvider.SessionSuspend,
			new Protocol.Capabilities.VideoStreamProvider.VideoStreamSessionArguments
			{
				SessionId = sessionId, ProviderId = providerId
			});

	public static string? Reason(CapabilityInvocationResult result)
		=> result.Error?.Details?.GetValueOrDefault("reason");

	public static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail("The condition was never met.");
	}
}
