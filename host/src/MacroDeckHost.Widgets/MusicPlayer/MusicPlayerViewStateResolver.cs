using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.References;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// Turns whatever the host currently knows about a music player into the one value the view draws. One
/// instance per session: the artwork it registered and the palette it derived are remembered here, so a
/// state change that leaves the cover alone costs no fetch and no re-encode.
/// </summary>
internal sealed class MusicPlayerViewStateResolver
{
	// The rendition asked of the artwork service. 256 is the largest variant it prepares, and a cover is
	// drawn at most at the widget's own basis - a deck tile, not a photo viewer.
	private const int ArtworkSize = 256;

	// How far the reported position may sit from the one the reader is already extrapolating before the
	// reference is re-anchored. Below this the widget is already showing the right second, so re-anchoring
	// would spend a patch to change nothing - which is the whole point of the reference (#749).
	private const double DriftToleranceMs = 1500;

	private readonly IMusicPlayerRegistry _registry;
	private readonly IMusicPlayerStateCache _stateCache;
	private readonly IMusicPlayerArtworkService _artworkService;
	private readonly IArtworkPaletteExtractor _paletteExtractor;
	private readonly IIntegrationRegistry _integrations;
	private readonly IUiResourceStore _resources;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	// The cover this resolver last registered, so an unchanged track never refetches it.
	private string? _artworkKey;
	private UiResource? _artwork;
	private ArtworkPalette? _palette;

	// The provider icon this resolver last registered. Cached because a refresh runs on every state
	// change - twice a second while a provider is being polled hard - and re-reading and re-hashing the
	// same bytes each time would be the most expensive thing this class does.
	private string? _providerIconKey;
	private UiResource? _providerIcon;

	public MusicPlayerViewStateResolver(
		IMusicPlayerRegistry registry,
		IMusicPlayerStateCache stateCache,
		IMusicPlayerArtworkService artworkService,
		IArtworkPaletteExtractor paletteExtractor,
		IIntegrationRegistry integrations,
		IUiResourceStore resources,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_registry = registry;
		_stateCache = stateCache;
		_artworkService = artworkService;
		_paletteExtractor = paletteExtractor;
		_integrations = integrations;
		_resources = resources;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<MusicPlayerViewStateResolver>();
	}

	public async Task<MusicPlayerViewState> ResolveAsync(
		MusicPlayerWidgetData config,
		MusicPlayerViewState previous,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(previous);

		var instances = _registry.GetInstances();
		var configured = string.IsNullOrEmpty(config.InstanceId) ? null : config.InstanceId;
		var missing = configured is not null &&
			instances.Count > 0 &&
			!instances.Any(instance => string.Equals(instance.InstanceId, configured, StringComparison.Ordinal));

		var instanceId = missing
			? instances[0].InstanceId
			: configured ?? (instances.Count > 0 ? instances[0].InstanceId : null);

		var descriptor = instanceId is null
			? null
			: instances.FirstOrDefault(instance
				=> string.Equals(instance.InstanceId, instanceId, StringComparison.Ordinal));

		var payload = instanceId is null ? null : _stateCache.GetState(instanceId);

		if (payload is null)
		{
			// No read has come back yet. Everything below needs one, so the widget says it is still
			// waiting rather than claiming a player it has not reached is disconnected.
			return new MusicPlayerViewState
			{
				IsLoading = true,
				InstanceMissing = missing,
				Label = LabelFor(descriptor),
				ProviderIcon = ResolveProviderIcon(descriptor),
			};
		}

		await ResolveArtworkAsync(instanceId!, payload, cancellationToken).ConfigureAwait(false);

		return new MusicPlayerViewState
		{
			IsLoading = false,
			InstanceMissing = missing,
			Label = LabelFor(descriptor),
			ProviderIcon = ResolveProviderIcon(descriptor),
			Artwork = _artwork,
			Accent = _palette?.Accent,
			Background = _palette?.Background,
			IsConnected = payload.IsConnected,
			IsUnavailable = !payload.IsConnected && payload.IsUnavailable,
			IsPlaying = payload.IsPlaying,
			IsPaused = payload.IsConnected &&
				string.Equals(payload.PlaybackState, "paused", StringComparison.Ordinal),
			TrackName = payload.TrackName,
			ArtistName = payload.ArtistName,
			AlbumName = payload.AlbumName,
			StatusMessage = payload.StatusMessage,
			Position = ResolvePosition(payload, previous),
		};
	}

	/// <summary>
	/// Where playback has reached, as a reference the reader carries forward on its own clock. Keeps the
	/// reference it already published whenever the newly reported position is within
	/// <see cref="DriftToleranceMs" /> of the one that reference already predicts - the widget is showing
	/// the right second either way, and an identical value emits no patch at all.
	/// </summary>
	private UiProgressReference? ResolvePosition(
		MusicPlayerStatePayload payload,
		MusicPlayerViewState previous)
	{
		if (payload.PositionMs is not { } reportedRaw)
		{
			return null;
		}

		var reported = Math.Max(0, reportedRaw);
		var duration = payload.DurationMs is { } value and > 0 ? value : (long?)null;
		var advancing = payload is { IsConnected: true, IsPlaying: true };
		var now = _timeProvider.GetUtcNow();

		if (previous.Position is { } prior &&
			prior.DurationMs == duration &&
			(prior.Rate is null) == advancing &&
			string.Equals(previous.TrackName, payload.TrackName, StringComparison.Ordinal))
		{
			var elapsed = advancing ? (now - prior.Anchor).TotalMilliseconds : 0;
			var predicted = prior.PositionMs + elapsed;

			if (Math.Abs(predicted - reported) <= DriftToleranceMs)
			{
				return prior;
			}
		}

		return advancing
			? UiProgressReference.Advancing(reported, now, duration)
			: UiProgressReference.Halted(reported, now, duration);
	}

	private async Task ResolveArtworkAsync(
		string instanceId,
		MusicPlayerStatePayload payload,
		CancellationToken cancellationToken)
	{
		if (payload.ArtworkId is not { Length: > 0 } artworkId)
		{
			_artworkKey = null;
			_artwork = null;
			_palette = null;

			return;
		}

		var key = $"{instanceId}\n{artworkId}";

		if (string.Equals(_artworkKey, key, StringComparison.Ordinal))
		{
			return;
		}

		var image = await LoadArtworkAsync(instanceId, artworkId, cancellationToken).ConfigureAwait(false);

		if (image is null)
		{
			_artworkKey = key;
			_artwork = null;
			_palette = null;

			return;
		}

		_artworkKey = key;
		// One slot per instance rather than per cover: an instance shows one track at a time, so the slot
		// is overwritten instead of accumulating, and the URL a renderer builds carries the content hash -
		// so replacing the bytes under one id still busts its cache.
		_artwork = _resources.Register(new UiResourceRegistration
		{
			OwnerId = MusicPlayerWidgetIcons.OwnerId,
			Name = $"artwork.{Slug(instanceId)}",
			MediaType = image.ContentType,
			Content = image.Content,
		});
		_palette = _paletteExtractor.Extract(image.Content);
	}

	private async Task<ArtworkImageResult?> LoadArtworkAsync(
		string instanceId,
		string artworkId,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _artworkService.GetImage(instanceId, artworkId, ArtworkSize, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
#pragma warning disable CA1031 // A provider's artwork fetch is plugin-owned; its fault leaves the cover empty.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Debug(exception,
				"Failed to read artwork {ArtworkId} for music player instance {InstanceId}",
				artworkId,
				instanceId);

			return null;
		}
	}

	private UiResource? ResolveProviderIcon(MusicPlayerInstanceDescriptor? descriptor)
	{
		if (descriptor is not { HasIcon: true })
		{
			_providerIconKey = null;
			_providerIcon = null;

			return null;
		}

		if (string.Equals(_providerIconKey, descriptor.IntegrationId, StringComparison.Ordinal))
		{
			return _providerIcon;
		}

		_providerIconKey = descriptor.IntegrationId;
		_providerIcon = null;

		var integration = _integrations.Integrations
			.FirstOrDefault(candidate
				=> string.Equals(candidate.Id, descriptor.IntegrationId, StringComparison.Ordinal));

		if (integration is not IIntegrationIconProvider iconProvider)
		{
			return null;
		}

		var bytes = iconProvider.GetIcon();

		if (bytes.Length == 0)
		{
			return null;
		}

		_providerIcon = _resources.Register(new UiResourceRegistration
		{
			OwnerId = MusicPlayerWidgetIcons.OwnerId,
			Name = $"provider.{Slug(descriptor.IntegrationId)}",
			MediaType = iconProvider.IconMimeType,
			Content = bytes,
		});

		return _providerIcon;
	}

	private static LocalizedText LabelFor(MusicPlayerInstanceDescriptor? descriptor)
	{
		if (descriptor is null)
		{
			return default;
		}

		return descriptor.DisplayName is { Length: > 0 } displayName
			? LocalizedText.FromLiteral(displayName)
			: descriptor.ProviderName;
	}

	/// <summary>A resource-id-safe stand-in for an arbitrary identifier: an instance or integration id is
	/// provider-chosen and need not match the resource grammar, so it is hashed rather than sanitised -
	/// sanitising could map two different ids onto one slot.</summary>
	private static string Slug(string value)
	{
		var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));

		return Convert.ToHexString(hash.AsSpan(0, 8)).ToLower(CultureInfo.InvariantCulture);
	}
}
