using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Integrations.Feeds.State;
using MacroDeckHost.Integrations.Feeds.Widgets;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Params;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Feeds.Events;

namespace MacroDeckHost.Integrations.Feeds.Events;

// Event ids and payload names are bound in users' triggers, so the spellings are frozen.
internal static class FeedEventIds
{
	public const string NewItem = "new-feed-item";

	public const string Updated = "feed-updated";

	public const string UpdateFailed = "feed-update-failed";

	public const string ItemOpened = "feed-item-opened";
}

internal static class FeedEventPayload
{
	public const string Feed = "feed";

	public const string FeedTitle = "feedTitle";

	public const string Title = "title";

	public const string Url = "url";

	public const string Description = "description";

	public const string Author = "author";

	public const string Published = "published";

	public const string Categories = "categories";

	public const string Error = "error";

	public const string Widget = "widget";
}

internal sealed record FeedEvent(string Id, IReadOnlyDictionary<string, object?> Payload);

internal static class FeedEventDefinitions
{
	public static IReadOnlyList<EventDefinition> All { get; } =
	[
		Article(FeedEventIds.NewItem, Strings.NewItemName(), Strings.NewItemDescription(), []),
		Article(FeedEventIds.Updated, Strings.UpdatedName(), Strings.UpdatedDescription(), []),
		new()
		{
			Id = FeedEventIds.UpdateFailed,
			Name = Strings.UpdateFailedName(),
			Description = Strings.UpdateFailedDescription(),
			Category = Strings.Category(),
			ConfigurationParameters = [FeedParameter()],
			PayloadParameters =
			[
				FeedParameter(),
				ActionParameter.Text(FeedEventPayload.FeedTitle, Params.FeedTitle()),
				ActionParameter.Text(FeedEventPayload.Error, Params.Error(), Params.ErrorDescription())
			]
		},
		Article(FeedEventIds.ItemOpened,
			Strings.ItemOpenedName(),
			Strings.ItemOpenedDescription(),
			[WidgetParameter()])
	];

	public static FeedEvent NewItem(FeedSnapshot feed, FeedItem item)
	{
		return new FeedEvent(FeedEventIds.NewItem, ArticlePayload(feed, item));
	}

	public static FeedEvent Updated(FeedSnapshot feed)
	{
		return new FeedEvent(FeedEventIds.Updated, ArticlePayload(feed, feed.Items.Count > 0 ? feed.Items[0] : null));
	}

	public static FeedEvent UpdateFailed(FeedSnapshot feed, string error)
	{
		return new FeedEvent(FeedEventIds.UpdateFailed,
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				[FeedEventPayload.Feed] = feed.EntryId.ToString("D"),
				[FeedEventPayload.FeedTitle] = feed.Title,
				[FeedEventPayload.Error] = error
			});
	}

	public static FeedEvent ItemOpened(FeedSnapshot feed, FeedItem item, Guid? widgetId)
	{
		var payload = ArticlePayload(feed, item);
		payload[FeedEventPayload.Widget] = widgetId?.ToString("D") ?? string.Empty;
		return new FeedEvent(FeedEventIds.ItemOpened, payload);
	}

	private static Dictionary<string, object?> ArticlePayload(FeedSnapshot feed, FeedItem? item)
	{
		return new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			[FeedEventPayload.Feed] = feed.EntryId.ToString("D"),
			[FeedEventPayload.FeedTitle] = feed.Title,
			[FeedEventPayload.Title] = item?.Title ?? string.Empty,
			[FeedEventPayload.Url] = item?.Url ?? string.Empty,
			[FeedEventPayload.Description] = item?.Description ?? string.Empty,
			[FeedEventPayload.Author] = item?.Author ?? string.Empty,
			[FeedEventPayload.Published]
				= item?.Published?.ToString("o", CultureInfo.InvariantCulture) ?? string.Empty,
			[FeedEventPayload.Categories] = item is null ? string.Empty : string.Join(", ", item.Categories)
		};
	}

	private static EventDefinition Article(
		string id,
		LocalizedText name,
		LocalizedText description,
		IReadOnlyList<ActionParameter> extra)
	{
		return new EventDefinition
		{
			Id = id,
			Name = name,
			Description = description,
			Category = Strings.Category(),
			ConfigurationParameters = [FeedParameter(), .. extra],
			PayloadParameters =
			[
				FeedParameter(),
				ActionParameter.Text(FeedEventPayload.FeedTitle, Params.FeedTitle()),
				ActionParameter.Text(FeedEventPayload.Title, Params.Title()),
				ActionParameter.Text(FeedEventPayload.Url, Params.Url()),
				ActionParameter.Text(FeedEventPayload.Description, Params.Description()),
				ActionParameter.Text(FeedEventPayload.Author, Params.Author()),
				ActionParameter.Text(FeedEventPayload.Published, Params.Published()),
				ActionParameter.Text(FeedEventPayload.Categories, Params.Categories()),
				.. extra.Select(parameter => ActionParameter.Text(parameter.Name, parameter.Label))
			]
		};
	}

	private static ActionParameter FeedParameter()
	{
		return ActionParameter.DynamicChoice(FeedEventPayload.Feed,
			Params.Feed(),
			Params.FeedDescription(),
			placeholder: Params.AllFeeds());
	}

	private static ActionParameter WidgetParameter()
	{
		return ActionParameter.WidgetTarget(FeedEventPayload.Widget,
			new WidgetTargetOptions
			{
				Label = Params.Widget(),
				Description = Params.WidgetDescription(),
				Required = false,
				WidgetTypes = [FeedWidgetTypes.ListQualifiedId, FeedWidgetTypes.FeaturedQualifiedId]
			});
	}
}
