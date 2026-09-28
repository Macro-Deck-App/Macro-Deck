using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Plugins.Capabilities;

namespace MacroDeckHost.Application.VideoStreams;

internal sealed class RemoteVideoStreamEndpoint : IVideoStreamEndpoint, IDisposable
{
	public const int MaxConcurrentOperations = 8;

	public const int MaxConcurrentCloses = 4;

	public const int MaxQueuedOperations = 256;

	private readonly SemaphoreSlim _closes = new(MaxConcurrentCloses);
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly SemaphoreSlim _operations = new(MaxConcurrentOperations);
	private readonly string _pluginId;
	private readonly TimeSpan _queueTimeout;
	private int _queued;

	public RemoteVideoStreamEndpoint(string pluginId, IPluginCapabilityInvoker invoker, TimeSpan? queueTimeout = null)
	{
		_pluginId = pluginId;
		_invoker = invoker;
		_queueTimeout = queueTimeout ?? ProtocolTimeouts.CapabilityInvoke;
	}

	public void Dispose()
	{
		_operations.Dispose();
		_closes.Dispose();
	}

	public async Task<IReadOnlyList<VideoStreamProviderInfo>> DescribeAsync(CancellationToken cancellationToken)
	{
		var result = await InvokeAsync<VideoStreamProviderDescribePayload>(
				CapabilityOperations.VideoStreamProvider.Describe,
				null,
				() => true,
				cancellationToken)
			.ConfigureAwait(false);

		return
		[
			.. (result?.Providers ?? [])
				.Where(provider => provider is not null)
				.Select(provider => new VideoStreamProviderInfo(provider.Id,
					provider.Name,
					provider.Description,
					provider.RegistrationId))
		];
	}

	public async Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(string providerId,
		CancellationToken cancellationToken)
	{
		var result = await InvokeAsync<VideoStreamProviderStreamsResult>(CapabilityOperations.VideoStreamProvider.Streams,
				new VideoStreamProviderStreamsArguments { ProviderId = providerId },
				() => true,
				cancellationToken)
			.ConfigureAwait(false);

		return [.. (result?.Streams ?? []).Where(stream => stream is not null).Select(VideoStreamWire.ToDescriptor)];
	}

	public async Task<VideoStreamOpenResult> OpenAsync(string providerId,
		VideoStreamOpenRequest request,
		Func<bool> proceed,
		CancellationToken cancellationToken)
	{
		var result = await InvokeAsync<VideoStreamSessionOpenResult>(CapabilityOperations.VideoStreamProvider.SessionOpen,
				new VideoStreamSessionOpenArguments
				{
					SessionId = request.SessionId,
					ProviderId = providerId,
					StreamId = request.StreamId,
					AcceptedTransports = request.AcceptedTransports,
					Consumer = VideoStreamWire.ToDto(request.Consumer)
				},
				proceed,
				cancellationToken)
			.ConfigureAwait(false);

		if (result?.Description is null || string.IsNullOrEmpty(result.RegistrationId))
		{
			throw new VideoStreamEndpointException(VideoStreamEndpointFailure.Rejected,
				VideoStreamErrorCode.Failed,
				"The provider answered session.open without a description.");
		}

		return new VideoStreamOpenResult(VideoStreamWire.ToDescription(result.Description), result.RegistrationId);
	}

	public Task SuspendAsync(string providerId, string sessionId, Func<bool> proceed, CancellationToken cancellationToken)
		=> InvokeAsync<object>(CapabilityOperations.VideoStreamProvider.SessionSuspend,
			new VideoStreamSessionArguments { SessionId = sessionId, ProviderId = providerId },
			proceed,
			cancellationToken);

	public async Task<VideoStreamSessionDescription?> ResumeAsync(string providerId,
		string sessionId,
		Func<bool> proceed,
		CancellationToken cancellationToken)
	{
		var result = await InvokeAsync<VideoStreamSessionResumeResult>(
				CapabilityOperations.VideoStreamProvider.SessionResume,
				new VideoStreamSessionArguments { SessionId = sessionId, ProviderId = providerId },
				proceed,
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Description is { } description ? VideoStreamWire.ToDescription(description) : null;
	}

	public async Task<VideoStreamSignal?> SignalAsync(string providerId,
		string sessionId,
		VideoStreamSignal signal,
		Func<bool> proceed,
		CancellationToken cancellationToken)
	{
		var result = await InvokeAsync<VideoStreamSessionSignalResult>(
				CapabilityOperations.VideoStreamProvider.SessionSignal,
				new VideoStreamSessionSignalArguments
				{
					SessionId = sessionId, ProviderId = providerId, Signal = VideoStreamWire.ToDto(signal)
				},
				proceed,
				cancellationToken)
			.ConfigureAwait(false);

		return result?.Signal is { } answer ? VideoStreamWire.ToSignal(answer) : null;
	}

	public async Task CloseAsync(string providerId,
		string sessionId,
		VideoStreamSessionReason reason,
		CancellationToken cancellationToken)
	{
		await _closes.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await CallAsync(CapabilityOperations.VideoStreamProvider.SessionClose,
					new VideoStreamSessionCloseArguments
					{
						SessionId = sessionId, ProviderId = providerId, Reason = reason.ToString()
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_closes.Release();
		}
	}

	private async Task<T?> InvokeAsync<T>(string operation,
		object? arguments,
		Func<bool> proceed,
		CancellationToken cancellationToken)
		where T : class
	{
		if (Interlocked.Increment(ref _queued) > MaxQueuedOperations + MaxConcurrentOperations)
		{
			Interlocked.Decrement(ref _queued);
			throw Busy();
		}

		bool entered;
		try
		{
			entered = await _operations.WaitAsync(_queueTimeout, cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			Interlocked.Decrement(ref _queued);
			throw;
		}

		if (!entered)
		{
			Interlocked.Decrement(ref _queued);
			throw Busy();
		}

		try
		{
			if (!proceed())
			{
				throw VideoStreamEndpointException.Skipped();
			}

			var data = await CallAsync(operation, arguments, cancellationToken).ConfigureAwait(false);
			try
			{
				return data?.Deserialize<T>(PluginProtocolJson.Options);
			}
			catch (JsonException)
			{
				throw new VideoStreamEndpointException(VideoStreamEndpointFailure.Rejected,
					VideoStreamErrorCode.Failed,
					$"The provider answered {operation} with a malformed result.");
			}
		}
		finally
		{
			_operations.Release();
			Interlocked.Decrement(ref _queued);
		}
	}

	private async Task<JsonElement?> CallAsync(string operation, object? arguments, CancellationToken cancellationToken)
	{
		try
		{
			return await _invoker.InvokeAsync(_pluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.VideoStreamProvider,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = operation,
						Arguments = arguments
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (RemoteCapabilityException exception)
		{
			throw Map(exception);
		}
	}

	private static VideoStreamEndpointException Map(RemoteCapabilityException exception)
	{
		var reason = exception.Details?.GetValueOrDefault("reason");
		if ((reason is not null && reason.StartsWith("video_stream_", StringComparison.Ordinal)) ||
			string.Equals(exception.Code, ProtocolErrorCodes.CapabilityUnsupported, StringComparison.Ordinal))
		{
			return new VideoStreamEndpointException(VideoStreamEndpointFailure.Rejected,
				VideoStreamWire.FromError(exception.Code, reason),
				exception.Message);
		}

		return exception.Code switch
		{
			ProtocolErrorCodes.RateLimited or ProtocolErrorCodes.QueueOverflow => new VideoStreamEndpointException(
				VideoStreamEndpointFailure.RateLimited,
				VideoStreamErrorCode.Busy,
				exception.Message),
			ProtocolErrorCodes.Timeout => new VideoStreamEndpointException(VideoStreamEndpointFailure.TimedOut,
				VideoStreamErrorCode.Failed,
				exception.Message),
			ProtocolErrorCodes.CapabilityUnavailable => new VideoStreamEndpointException(
				VideoStreamEndpointFailure.Unavailable,
				VideoStreamErrorCode.Unsupported,
				exception.Message),
			_ => new VideoStreamEndpointException(VideoStreamEndpointFailure.Rejected,
				VideoStreamErrorCode.Failed,
				exception.Message)
		};
	}

	private static VideoStreamEndpointException Busy()
		=> new(VideoStreamEndpointFailure.Rejected,
			VideoStreamErrorCode.Busy,
			"The plugin is already handling as many video stream calls as it may.");
}
