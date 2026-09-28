using MacroDeck.Localization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="IVideoStreamProviderContext" /> call, or which <c>video-streams</c> host api
/// operation, a <see cref="VideoStreamProviderCall" /> recorded.</summary>
public enum VideoStreamProviderCallKind
{
	/// <summary>Recorded by <see cref="IVideoStreamProviderContext.RegisterProviderAsync" />.</summary>
	Register,

	/// <summary>Recorded by <see cref="IVideoStreamProviderContext.UnregisterProviderAsync" />.</summary>
	Unregister,

	/// <summary>A plugin hosted over the wire told the host its provider table changed, which is what a
	/// registration or withdrawal sends.</summary>
	ProvidersChanged,

	/// <summary>Recorded by <see cref="IVideoStreamProviderContext.NotifyStreamsChangedAsync" />.</summary>
	StreamsChanged,

	/// <summary>Recorded by <see cref="IVideoStreamProviderContext.UpdateSessionAsync" />.</summary>
	SessionUpdate,

	/// <summary>Recorded by <see cref="IVideoStreamProviderContext.SendSignalAsync" />.</summary>
	Signal,

	/// <summary>Recorded by <see cref="IVideoStreamProviderContext.CloseSessionAsync" />.</summary>
	SessionClose
}

/// <summary>One call recorded by <see cref="FakeVideoStreamProviderContext" />. Only the members the call
/// carries are set.</summary>
public sealed record VideoStreamProviderCall
{
	/// <summary>Which call was made.</summary>
	public required VideoStreamProviderCallKind Kind { get; init; }

	/// <summary>The provider-local id the call addressed, for provider calls.</summary>
	public string? ProviderId { get; init; }

	/// <summary>The session the call addressed, for session calls.</summary>
	public string? SessionId { get; init; }

	/// <summary>The reported state, for <see cref="VideoStreamProviderCallKind.SessionUpdate" />.</summary>
	public VideoStreamSessionState? State { get; init; }

	/// <summary>The replacement description, for <see cref="VideoStreamProviderCallKind.SessionUpdate" />.</summary>
	public VideoStreamSessionDescription? Description { get; init; }

	/// <summary>The reason, for <see cref="VideoStreamProviderCallKind.SessionUpdate" /> and
	/// <see cref="VideoStreamProviderCallKind.SessionClose" />.</summary>
	public VideoStreamSessionReason? Reason { get; init; }

	/// <summary>The message the consumer may show, when the call carried one.</summary>
	public LocalizedText? Message { get; init; }

	/// <summary>The signal, for <see cref="VideoStreamProviderCallKind.Signal" />.</summary>
	public VideoStreamSignal? Signal { get; init; }
}

/// <summary>
/// In-memory <see cref="IVideoStreamProviderContext" /> that records every call and applies the same
/// registration and size rules the SDK and the host apply: an invalid or duplicate provider id, a
/// seventeenth provider, and a description or signal past a documented bound are rejected with an
/// <see cref="ArgumentException" />. Unregistering an unknown id is a silent no-op.
/// </summary>
/// <remarks>
/// It does not open sessions: drive a provider's sessions through
/// <see cref="VideoStreamProviderTestClient" />, the way the host does.
/// </remarks>
public sealed class FakeVideoStreamProviderContext : IVideoStreamProviderContext
{
	/// <summary>The plugin id the qualified ids this fake hands out are built from.</summary>
	public const string PluginId = "test-plugin";

	private readonly List<VideoStreamProviderCall> _calls = [];
	private readonly Dictionary<string, IVideoStreamProvider> _providers = new(StringComparer.Ordinal);
	private readonly Lock _sync = new();

	/// <summary>Every call made against this context, in order.</summary>
	public IReadOnlyList<VideoStreamProviderCall> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>The providers currently registered, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, IVideoStreamProvider> Providers
	{
		get
		{
			lock (_sync)
			{
				return new Dictionary<string, IVideoStreamProvider>(_providers, StringComparer.Ordinal);
			}
		}
	}

	/// <inheritdoc />
	public Task<VideoStreamProviderRegistration> RegisterProviderAsync(
		IVideoStreamProvider provider,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(provider);
		var id = provider.Id;
		if (!MacroDeckId.TryValidateLocalId(id, LocalIdKind.Resource, out var error))
		{
			throw new ArgumentException($"'{id}' is not a valid video stream provider id: {error}", nameof(provider));
		}

		if (provider.Name.IsEmpty)
		{
			throw new ArgumentException("The provider name must not be empty.", nameof(provider));
		}

		lock (_sync)
		{
			if (_providers.ContainsKey(id))
			{
				throw new ArgumentException($"A video stream provider with id '{id}' is already registered.",
					nameof(provider));
			}

			if (_providers.Count >= VideoStreamLimits.MaxProvidersPerPlugin)
			{
				throw new ArgumentException(
					$"A plugin can register at most {VideoStreamLimits.MaxProvidersPerPlugin} video stream providers.",
					nameof(provider));
			}

			_providers[id] = provider;
			_calls.Add(new VideoStreamProviderCall { Kind = VideoStreamProviderCallKind.Register, ProviderId = id });
		}

		return Task.FromResult(new VideoStreamProviderRegistration(PluginId + QualifiedId.Separator + id, id));
	}

	/// <inheritdoc />
	public Task UnregisterProviderAsync(string providerId, CancellationToken cancellationToken = default)
	{
		lock (_sync)
		{
			_providers.Remove(providerId);
			_calls.Add(new VideoStreamProviderCall
			{
				Kind = VideoStreamProviderCallKind.Unregister, ProviderId = providerId
			});
		}

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task NotifyStreamsChangedAsync(string providerId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(providerId);
		Record(new VideoStreamProviderCall { Kind = VideoStreamProviderCallKind.StreamsChanged, ProviderId = providerId });
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task UpdateSessionAsync(
		string sessionId,
		VideoStreamSessionState state,
		VideoStreamSessionDescription? description = null,
		VideoStreamSessionReason reason = VideoStreamSessionReason.None,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		if (description is not null)
		{
			ValidateDescription(description);
		}

		Record(new VideoStreamProviderCall
		{
			Kind = VideoStreamProviderCallKind.SessionUpdate,
			SessionId = sessionId,
			State = state,
			Description = description,
			Reason = reason,
			Message = message
		});
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task SendSignalAsync(string sessionId, VideoStreamSignal signal, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		ArgumentNullException.ThrowIfNull(signal);
		if (string.IsNullOrEmpty(signal.Type) || signal.Type.Length > VideoStreamLimits.MaxSignalTypeLength)
		{
			throw new ArgumentException(
				$"A signal type must be 1 to {VideoStreamLimits.MaxSignalTypeLength} characters.", nameof(signal));
		}

		if (signal.Payload is null || signal.Payload.Length > VideoStreamLimits.MaxSignalPayloadLength)
		{
			throw new ArgumentException(
				$"A signal payload must be present and at most {VideoStreamLimits.MaxSignalPayloadLength} characters.",
				nameof(signal));
		}

		Record(new VideoStreamProviderCall
		{
			Kind = VideoStreamProviderCallKind.Signal, SessionId = sessionId, Signal = signal
		});
		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public Task CloseSessionAsync(
		string sessionId,
		VideoStreamSessionReason reason = VideoStreamSessionReason.ProviderClosed,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		Record(new VideoStreamProviderCall
		{
			Kind = VideoStreamProviderCallKind.SessionClose, SessionId = sessionId, Reason = reason, Message = message
		});
		return Task.CompletedTask;
	}

	internal void RecordProvidersChanged()
		=> Record(new VideoStreamProviderCall { Kind = VideoStreamProviderCallKind.ProvidersChanged });

	private static void ValidateDescription(VideoStreamSessionDescription description)
	{
		if (!VideoStreamLimits.IsValidTransport(description.Transport))
		{
			throw new ArgumentException($"'{description.Transport}' is not a valid transport token.",
				nameof(description));
		}

		if (description.Url is { Length: > VideoStreamLimits.MaxUrlLength } ||
			description.Payload is { Length: > VideoStreamLimits.MaxDescriptionPayloadLength })
		{
			throw new ArgumentException("The description's url or payload is too long.", nameof(description));
		}

		if (description.Parameters is { } parameters &&
			(parameters.Count > VideoStreamLimits.MaxMapEntries ||
				parameters.Any(pair => string.IsNullOrEmpty(pair.Key) ||
					pair.Key.Length > VideoStreamLimits.MaxMapKeyLength ||
					pair.Value is null ||
					pair.Value.Length > VideoStreamLimits.MaxMapValueLength)))
		{
			throw new ArgumentException("The description's parameters exceed a documented bound.", nameof(description));
		}
	}

	private void Record(VideoStreamProviderCall call)
	{
		lock (_sync)
		{
			_calls.Add(call);
		}
	}
}
