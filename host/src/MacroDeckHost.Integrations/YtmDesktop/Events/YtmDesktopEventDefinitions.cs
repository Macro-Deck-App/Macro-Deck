using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.YtmDesktop.Events;

internal static class YtmDesktopEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		new()
		{
			Id = YtmDesktopEventIds.LikeChanged,
			Name = AppStrings.Integrations.YtmDesktop.Events.RatingChangedName(),
			Description = AppStrings.Integrations.YtmDesktop.Events.RatingChangedDescription(),
			Category = AppStrings.Integrations.YtmDesktop.Events.PlaybackCategory(),
			ConfigurationParameters =
			[
				ActionParameter.Choice("likeStatus",
					[
						new ActionParameterOption
							{ Value = string.Empty, Label = AppStrings.Integrations.YtmDesktop.Events.AnyRating() },
						new ActionParameterOption
							{ Value = "like", Label = AppStrings.Integrations.YtmDesktop.Events.Liked() },
						new ActionParameterOption
							{ Value = "dislike", Label = AppStrings.Integrations.YtmDesktop.Events.Disliked() },
						new ActionParameterOption
						{
							Value = "indifferent", Label = AppStrings.Integrations.YtmDesktop.Events.RatingCleared()
						}
					],
					AppStrings.Integrations.YtmDesktop.Events.RatingLabel(),
					AppStrings.Integrations.YtmDesktop.Events.RatingDescription(),
					string.Empty)
			],
			PayloadParameters =
			[
				ActionParameter.Text("likeStatus", AppStrings.Integrations.YtmDesktop.Events.RatingLabel()),
				ActionParameter.Text("previousLikeStatus",
					AppStrings.Integrations.YtmDesktop.Events.PreviousRatingLabel()),
				ActionParameter.Text("trackName", AppStrings.Integrations.YtmDesktop.Events.TrackLabel()),
				ActionParameter.Text("videoId", AppStrings.Integrations.YtmDesktop.Events.VideoIdLabel())
			]
		}
	];
}
