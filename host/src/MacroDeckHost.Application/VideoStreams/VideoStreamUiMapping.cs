using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Ui.Transport.Messages.VideoStreams;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.VideoStreams;

public static class VideoStreamUiMapping
{
	public static string WireName<T>(T value)
		where T : struct, Enum
		=> JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());

	public static LocalizedText ErrorText(VideoStreamError error)
		=> error switch
		{
			VideoStreamError.Busy => AppStrings.Errors.VideoStream.Busy(),
			VideoStreamError.UnknownProvider => AppStrings.Errors.VideoStream.UnknownProvider(),
			VideoStreamError.UnknownStream => AppStrings.Errors.VideoStream.UnknownStream(),
			VideoStreamError.UnknownSession => AppStrings.Errors.VideoStream.UnknownSession(),
			VideoStreamError.SessionLimitReached => AppStrings.Errors.VideoStream.SessionLimitReached(),
			VideoStreamError.PayloadTooLarge => AppStrings.Errors.VideoStream.PayloadTooLarge(),
			VideoStreamError.StreamUnavailable => AppStrings.Errors.VideoStream.StreamUnavailable(),
			VideoStreamError.TransportNotAccepted => AppStrings.Errors.VideoStream.TransportNotAccepted(),
			VideoStreamError.SignalingUnsupported => AppStrings.Errors.VideoStream.SignalingUnsupported(),
			VideoStreamError.ProviderUnavailable => AppStrings.Errors.VideoStream.ProviderUnavailable(),
			_ => AppStrings.Errors.VideoStream.Failed()
		};

	public static GetVideoStreamsResponse ToResponse(IReadOnlyList<VideoStreamProviderEntry> providers)
		=> new()
		{
			Providers =
			[
				.. providers.Select(provider => new VideoStreamProviderItem
				{
					Id = provider.QualifiedId,
					Name = provider.Name,
					Description = provider.Description,
					Streams =
					[
						.. provider.Streams.Select(stream => new VideoStreamItem
						{
							Id = stream.Id,
							Name = stream.Name,
							Description = stream.Description,
							Width = stream.Width,
							Height = stream.Height,
							HasAudio = stream.HasAudio,
							State = WireName(stream.State)
						})
					]
				})
			]
		};

	public static VideoStreamSignalMessage ToMessage(VideoStreamSignal signal)
		=> new() { Type = signal.Type, Payload = signal.Payload };

	public static VideoStreamDescriptionMessage? ToMessage(VideoStreamSessionDescription? description)
		=> description is null
			? null
			: new VideoStreamDescriptionMessage
			{
				Transport = description.Transport,
				Url = description.Url,
				Parameters = description.Parameters,
				Payload = description.Payload,
				ExpiresAt = description.ExpiresAt
			};
}
