using System.Text.Json;
using MacroDeck.Localization;
using MacroDeckHost.Application.Ui.Transport.Messages.VideoStreams;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.VideoStreams;

public static class VideoStreamUiMapping
{
	public static string WireName<T>(T value)
		where T : struct, Enum
	{
		return JsonNamingPolicy.SnakeCaseLower.ConvertName(value.ToString());
	}

	public static LocalizedText ErrorText(VideoStreamError error)
	{
		return error switch
		{
			VideoStreamError.Busy => AppStrings.Errors.VideoStream.Busy(),
			VideoStreamError.UnknownProvider => AppStrings.Errors.VideoStream.UnknownProvider(),
			VideoStreamError.UnknownStream => AppStrings.Errors.VideoStream.UnknownStream(),
			VideoStreamError.UnknownSession => AppStrings.Errors.VideoStream.UnknownSession(),
			VideoStreamError.SessionLimitReached => AppStrings.Errors.VideoStream.SessionLimitReached(),
			VideoStreamError.StreamUnavailable => AppStrings.Errors.VideoStream.StreamUnavailable(),
			VideoStreamError.TransportNotAccepted => AppStrings.Errors.VideoStream.TransportNotAccepted(),
			VideoStreamError.ProviderUnavailable => AppStrings.Errors.VideoStream.ProviderUnavailable(),
			_ => AppStrings.Errors.VideoStream.Failed()
		};
	}

	public static GetVideoStreamsResponse ToResponse(IReadOnlyList<VideoStreamProviderEntry> providers)
	{
		return new GetVideoStreamsResponse
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
	}

	public static VideoStreamDescriptionMessage? ToMessage(VideoStreamRelayDescription? description)
	{
		return description is null
			? null
			: new VideoStreamDescriptionMessage { Transport = description.Transport, Url = description.Url };
	}
}
