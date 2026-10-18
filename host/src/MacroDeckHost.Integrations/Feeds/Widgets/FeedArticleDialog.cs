using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Feeds.Connections;
using MacroDeckHost.Integrations.Feeds.Protocol;
using MacroDeckHost.Integrations.Feeds.State;
using MacroDeckHost.Integrations.Ui;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds.Dialog;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal sealed record FeedDialogContent(FeedItem? Item, string Meta, UiResource? Image);

internal static class FeedArticleDialog
{
	public const string EntryKey = "entry";

	public const string ItemKey = "item";

	public const string CanOpenKey = "canOpen";

	public const string CultureKey = "culture";

	private const string Separator = " · ";

	private static readonly ILogger _logger = IntegrationLog.For<FeedDialogContent>(FeedsIntegration.IntegrationId);

	private static readonly UiSize _titleText = UiSize.FromBasis(0.05);
	private static readonly UiSize _text = UiSize.FromBasis(0.042);
	private static readonly UiSize _smallText = UiSize.FromBasis(0.034);
	private static readonly UiSize _icon = UiSize.FromBasis(0.042);
	private static readonly UiSize _gap = UiSize.FromBasis(0.022);

	public static UiViewSession Create(UiSurface surface, IFeedSource source, IFeedImages images, TimeProvider time)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(images);

		var data = surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Data, out var element) ? element : default;
		var feed = Guid.TryParse(ReadString(data, EntryKey), out var entryId) ? source.Find(entryId) : null;
		var item = ReadString(data, ItemKey) is { } key ? feed?.Find(key) : null;
		var url = Uri.TryCreate(item?.Url, UriKind.Absolute, out var parsed) && FeedClient.IsWebUrl(parsed)
			? parsed
			: null;
		var culture = ReadString(data, CultureKey)
			?? (surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.Culture, out var dialogCulture)
				&& dialogCulture.ValueKind == JsonValueKind.String
					? dialogCulture.GetString()
					: null);
		var format = new FeedDateFormat(FeedDateFormat.CultureOf(culture), time.LocalTimeZone, time.GetUtcNow());
		var meta = feed is null || item is null
			? string.Empty
			: string.Join(Separator,
				new[] { feed.Title, item.Author ?? string.Empty, format.Long(item.Published) }
					.Where(part => part.Length > 0));

		var content = new FeedDialogContent(item, meta, item?.ImageUrl is { } imageUrl ? images.Find(imageUrl) : null);
		var canOpen = new UiState<bool>(url is not null && ReadBool(data, CanOpenKey));
		var feedback = new UiState<LocalizedText>(default);

		UiViewSession? session = null;
		UiView? view = null;
		view = new UiView(surface,
			Build(content,
				url?.AbsoluteUri,
				canOpen,
				feedback,
				() => _ = OpenAsync(session?.Origin, url, view!, canOpen, feedback)));
		session = new UiViewSession(view);
		return session;
	}

	private static async Task OpenAsync(
		IUiEventOrigin? origin,
		Uri? url,
		UiView view,
		UiState<bool> canOpen,
		UiState<LocalizedText> feedback)
	{
		var opened = false;
		if (origin is not null && url is not null)
		{
			try
			{
				opened = await origin.OpenUrlAsync(url).ConfigureAwait(false);
			}
#pragma warning disable CA1031 // A press has nobody to report to; the dialog shows the address instead.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				_logger.Warning(exception, "Opening a feed article from its dialog failed");
			}
		}

		if (opened)
		{
			return;
		}

		using (view.Batch())
		{
			canOpen.Set(false);
			feedback.Set(Strings.CannotOpen());
		}
	}

	private static UiStack Build(
		FeedDialogContent content,
		string? url,
		UiState<bool> canOpen,
		UiState<LocalizedText> feedback,
		Action open)
	{
		if (content.Item is not { } item)
		{
			return new UiStack
			{
				Key = "feedArticle",
				Direction = UiComponentDirections.Vertical,
				Padding = 0.04,
				Children =
				[
					new UiTextRun
					{
						Key = "missing",
						Text = Strings.Unavailable(),
						Size = _text,
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
						Wrap = true,
						Fill = true
					}
				]
			};
		}

		var children = new List<UiElement>();
		if (content.Image is { } image)
		{
			children.Add(new UiImage { Key = "image", Source = image, MainSize = UiSize.FromBasis(0.4) });
		}

		children.Add(new UiTextRun
		{
			Key = "title",
			Text = item.Title,
			Size = _titleText,
			Weight = UiComponentTextWeights.SemiBold,
			Wrap = true
		});

		if (content.Meta.Length > 0)
		{
			children.Add(new UiTextRun
			{
				Key = "meta",
				Text = content.Meta,
				Size = _smallText,
				Role = UiComponentTextRoles.Secondary,
				Wrap = true
			});
		}

		if (item.Description is { } description)
		{
			children.Add(new UiTextRun { Key = "description", Text = description, Size = _text, Wrap = true });
		}

		if (url is not null)
		{
			children.Add(new UiTextField { Key = "url", Text = url, Size = _smallText });
			children.Add(new UiWhen
			{
				Key = "openGate",
				Condition = () => canOpen.Value,
				Content = () => OpenButton(open)
			});
		}

		children.Add(new UiWhen
		{
			Key = "feedbackGate",
			Condition = () => !feedback.Value.IsEmpty,
			Content = () => new UiTextRun
			{
				Key = "feedback",
				Text = UiText.Optional(() => feedback.Value),
				Size = _smallText,
				Role = UiComponentTextRoles.Secondary,
				Wrap = true
			}
		});

		return new UiStack
		{
			Key = "feedArticle",
			Direction = UiComponentDirections.Vertical,
			Padding = 0.04,
			Children = [new UiList { Key = "content", Fill = true, Gap = _gap, Children = children }]
		};
	}

	private static UiButton OpenButton(Action open)
	{
		return new UiButton
		{
			Key = "open",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = _gap,
			Padding = _gap,
			Events = [UiEventHandler.On(UiComponentEvents.Press, open)],
			Children =
			[
				new UiIcon { Key = "icon", Icon = UiIcons.ExternalLink, Size = _icon, MainSize = _icon },
				new UiTextRun
				{
					Key = "label",
					Text = Strings.Open(),
					Size = _text,
					Weight = UiComponentTextWeights.Medium,
					MaxLines = 1
				}
			]
		};
	}

	private static string? ReadString(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	private static bool ReadBool(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.True;
	}
}
