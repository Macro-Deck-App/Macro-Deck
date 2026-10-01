using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.VideoStreams;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;

internal sealed class VideoStreamProviderCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	VideoStreamProviderRegistry registry,
	ILogger logger) : ICapabilityHandler
{
	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly bool _declared = integrations.OfType<IVideoStreamIntegration>().Any();
	private readonly ILogger _logger = logger.ForContext<VideoStreamProviderCapabilityHandler>();

	public string Kind => CapabilityKinds.VideoStreamProvider;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _declared
			?
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.VideoStreamProvider, LocalId = ProviderCapabilityId.LocalId,
					VersionRange = _version
				}
			]
			: [];

	public async Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// Captured before anything awaits, so an open that races a reconnect belongs to the connection it
		// arrived on and is swept with that connection, never with the next one.
		var epoch = registry.Sessions.CurrentEpoch;

		if (string.Equals(invocation.Operation, CapabilityOperations.VideoStreamProvider.Describe,
			StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Ok(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No video stream provider '{invocation.LocalId}' is registered in this plugin.");
		}

		try
		{
			return invocation.Operation switch
			{
				CapabilityOperations.VideoStreamProvider.Streams =>
					await StreamsAsync(Arguments<VideoStreamProviderStreamsArguments>(invocation), cancellationToken)
						.ConfigureAwait(false),
				CapabilityOperations.VideoStreamProvider.SessionOpen =>
					await OpenAsync(Arguments<VideoStreamSessionOpenArguments>(invocation), epoch, cancellationToken)
						.ConfigureAwait(false),
				CapabilityOperations.VideoStreamProvider.SessionSuspend =>
					await SuspendAsync(Arguments<VideoStreamSessionArguments>(invocation), cancellationToken)
						.ConfigureAwait(false),
				CapabilityOperations.VideoStreamProvider.SessionResume =>
					await ResumeAsync(Arguments<VideoStreamSessionArguments>(invocation), cancellationToken)
						.ConfigureAwait(false),
				CapabilityOperations.VideoStreamProvider.SessionClose =>
					await CloseAsync(Arguments<VideoStreamSessionCloseArguments>(invocation)).ConfigureAwait(false),
				_ => CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
					$"The video-stream-provider capability has no operation '{invocation.Operation}'.")
			};
		}
		catch (VideoStreamException exception)
		{
			return Failure(exception.ErrorCode, exception.Message);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "The video stream provider failed {Operation}", invocation.Operation);
			return Failure(VideoStreamErrorCode.Failed, "The video stream provider failed.");
		}
	}

	private VideoStreamProviderDescribePayload Describe()
		=> new()
		{
			Providers =
			[
				.. registry.Snapshot()
					.Select(registration => new VideoStreamProviderDto
					{
						Id = registration.ProviderId,
						Name = registration.Provider.Name,
						Description = registration.Provider.Description,
						RegistrationId = registration.RegistrationId
					})
			]
		};

	private async Task<CapabilityInvocationResult> StreamsAsync(
		VideoStreamProviderStreamsArguments? arguments,
		CancellationToken cancellationToken)
	{
		if (arguments is null)
		{
			return InvalidPayload("streams requires a providerId.");
		}

		if (!registry.TryGet(arguments.ProviderId, out var registration))
		{
			return UnknownProvider(arguments.ProviderId);
		}

		var streams = await registration.Provider.GetStreamsAsync(cancellationToken).ConfigureAwait(false);
		var valid = new List<VideoStreamDescriptorDto>();
		foreach (var stream in streams ?? [])
		{
			if (VideoStreamWire.ValidateDescriptor(stream) is { } problem)
			{
				_logger.Warning("Provider {ProviderId} listed a stream that was left out: {Problem}",
					registration.ProviderId,
					problem);
				continue;
			}

			if (valid.Count == VideoStreamLimits.MaxStreamsPerProvider)
			{
				_logger.Warning("Provider {ProviderId} lists more than {MaxStreams} streams; the rest are left out",
					registration.ProviderId,
					VideoStreamLimits.MaxStreamsPerProvider);
				break;
			}

			valid.Add(VideoStreamWire.ToDto(stream));
		}

		return CapabilityInvocationResult.Ok(new VideoStreamProviderStreamsResult { Streams = valid });
	}

	private async Task<CapabilityInvocationResult> OpenAsync(
		VideoStreamSessionOpenArguments? arguments,
		long epoch,
		CancellationToken cancellationToken)
	{
		if (arguments is null ||
			string.IsNullOrEmpty(arguments.SessionId) ||
			!VideoStreamLimits.IsValidStreamId(arguments.StreamId) ||
			arguments.AcceptedTransports is not { Count: > 0 } accepted ||
			!accepted.All(VideoStreamLimits.IsValidTransport))
		{
			return InvalidPayload(
				"session.open requires a sessionId, a valid streamId and at least one valid accepted transport.");
		}

		if (!registry.TryGet(arguments.ProviderId, out var registration))
		{
			return UnknownProvider(arguments.ProviderId);
		}

		var sessions = registry.Sessions;
		switch (sessions.TryBeginOpen(arguments.SessionId, registration.ProviderId, registration.Provider, epoch))
		{
			case VideoStreamOpenAdmission.Tombstoned:
				return Failure(VideoStreamErrorCode.UnknownSession, "The session was closed before it opened.");
			case VideoStreamOpenAdmission.Duplicate:
				return InvalidPayload("A session with this id is already open.");
		}

		// After the admission, so an unregister that removed the provider in between either shows here or
		// finds the opening session and defers its close.
		if (!registry.IsCurrent(registration))
		{
			sessions.FailOpen(arguments.SessionId);
			return UnknownProvider(arguments.ProviderId);
		}

		VideoStreamSessionDescription description;
		try
		{
			description = await registration.Provider.OpenAsync(new VideoStreamOpenRequest(arguments.SessionId,
						arguments.StreamId,
						accepted),
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch
		{
			sessions.FailOpen(arguments.SessionId);
			throw;
		}

		var wire = description is null ? null : VideoStreamWire.ToDto(description);
		var (problemCode, problem) = Check(wire, accepted);
		var completion = sessions.CompleteOpen(arguments.SessionId);

		switch (completion.Outcome)
		{
			case VideoStreamOpenOutcome.Open when problem is null:
				return CapabilityInvocationResult.Ok(new VideoStreamSessionOpenResult
				{
					Description = wire!,
					RegistrationId = registration.RegistrationId
				});

			case VideoStreamOpenOutcome.Open:
				_logger.Warning("Provider {ProviderId} opened session {SessionId} with an unusable description: {Problem}",
					registration.ProviderId,
					arguments.SessionId,
					problem);
				if (sessions.RequestClose(arguments.SessionId, VideoStreamSessionReason.Failed).Close is { } failed)
				{
					await registry.CloseAsync(failed).ConfigureAwait(false);
				}

				return Failure(problemCode, problem!);

			case VideoStreamOpenOutcome.CloseNow:
				await registry.CloseAsync(completion.Close!).ConfigureAwait(false);
				return Failure(VideoStreamErrorCode.UnknownSession, "The session was closed while it was opening.");

			default:
				return Failure(VideoStreamErrorCode.UnknownSession, "The session was closed while it was opening.");
		}
	}

	private async Task<CapabilityInvocationResult> SuspendAsync(
		VideoStreamSessionArguments? arguments,
		CancellationToken cancellationToken)
	{
		if (!TryResolveOpenSession(arguments?.SessionId, arguments?.ProviderId, out var provider, out var failure))
		{
			return failure;
		}

		await provider.SuspendAsync(arguments!.SessionId, cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private async Task<CapabilityInvocationResult> ResumeAsync(
		VideoStreamSessionArguments? arguments,
		CancellationToken cancellationToken)
	{
		if (!TryResolveOpenSession(arguments?.SessionId, arguments?.ProviderId, out var provider, out var failure))
		{
			return failure;
		}

		var description = await provider.ResumeAsync(arguments!.SessionId, cancellationToken).ConfigureAwait(false);
		var wire = description is null ? null : VideoStreamWire.ToDto(description);
		if (wire is not null && VideoStreamWire.ValidateDescription(wire) is { } problem)
		{
			_logger.Warning("Provider {ProviderId} resumed session {SessionId} with an unusable description: {Problem}",
				arguments.ProviderId,
				arguments.SessionId,
				problem);
			return Failure(VideoStreamErrorCode.Failed, problem);
		}

		return CapabilityInvocationResult.Ok(new VideoStreamSessionResumeResult
		{
			Description = wire
		});
	}

	// Idempotent by design: the host retries a close it could not deliver, and a close for a session
	// whose open has not arrived yet leaves a tombstone that refuses that open.
	private async Task<CapabilityInvocationResult> CloseAsync(VideoStreamSessionCloseArguments? arguments)
	{
		if (arguments is null || string.IsNullOrEmpty(arguments.SessionId))
		{
			return InvalidPayload("session.close requires a sessionId.");
		}

		var sessions = registry.Sessions;
		if (sessions.TryGet(arguments.SessionId, out var session) &&
			!string.Equals(session.ProviderId, arguments.ProviderId, StringComparison.Ordinal))
		{
			return Failure(VideoStreamErrorCode.UnknownSession, "The provider has no session with this id.");
		}

		var decision = sessions.RequestClose(arguments.SessionId, VideoStreamWire.ParseReason(arguments.Reason));
		if (decision.Close is { } closing)
		{
			await registry.CloseAsync(closing).ConfigureAwait(false);
		}

		return CapabilityInvocationResult.Ok();
	}

	private bool TryResolveOpenSession(string? sessionId,
		string? providerId,
		out IVideoStreamProvider provider,
		out CapabilityInvocationResult failure)
	{
		provider = null!;
		if (string.IsNullOrEmpty(sessionId))
		{
			failure = InvalidPayload("The operation requires a sessionId.");
			return false;
		}

		if (!registry.TryGet(providerId, out var registration))
		{
			failure = UnknownProvider(providerId);
			return false;
		}

		if (!registry.Sessions.TryGet(sessionId, out var session) ||
			session.Phase != VideoStreamSessionPhase.Open ||
			!string.Equals(session.ProviderId, registration.ProviderId, StringComparison.Ordinal))
		{
			failure = Failure(VideoStreamErrorCode.UnknownSession, "The provider has no open session with this id.");
			return false;
		}

		provider = registration.Provider;
		failure = null!;
		return true;
	}

	private static (VideoStreamErrorCode Code, string? Problem) Check(
		VideoStreamSessionDescriptionDto? description,
		IReadOnlyList<string> accepted)
	{
		if (VideoStreamWire.ValidateDescription(description) is { } problem)
		{
			return (VideoStreamErrorCode.Failed, problem);
		}

		return accepted.Contains(description!.Transport, StringComparer.Ordinal)
			? (VideoStreamErrorCode.Failed, null)
			: (VideoStreamErrorCode.TransportNotAccepted,
				$"The provider answered with transport '{description.Transport}', which the consumer does not accept.");
	}

	private static CapabilityInvocationResult Failure(VideoStreamErrorCode code, string message)
		=> CapabilityInvocationResult.Failed(VideoStreamWire.ToError(code, message));

	private static CapabilityInvocationResult UnknownProvider(string? providerId)
		=> Failure(VideoStreamErrorCode.UnknownProvider, $"No video stream provider '{providerId}' is registered.");

	private static CapabilityInvocationResult InvalidPayload(string message)
		=> CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload, message);

	private static T? Arguments<T>(CapabilityInvocation invocation)
		where T : class
	{
		try
		{
			return invocation.Arguments?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
