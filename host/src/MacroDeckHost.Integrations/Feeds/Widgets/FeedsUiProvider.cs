using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Feeds.Connections;
using MacroDeckHost.Integrations.Ui;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal sealed class FeedsUiProvider : IUiProvider
{
	public const string IconFolder = "widget";

	public const string Icon = "rss";

	private readonly IFeedSource _source;
	private readonly IFeedImages _images;
	private readonly Func<IUiResourceRegistry?> _resources;
	private readonly TimeProvider _time;

	public FeedsUiProvider(
		IFeedSource source,
		IFeedImages images,
		Func<IUiResourceRegistry?> resources,
		TimeProvider time)
	{
		_source = source;
		_images = images;
		_resources = resources;
		_time = time;
	}

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Preview, SessionMode = UiSessionModes.Shared },
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
		new() { Kind = UiSurfaceKinds.Dialog, SessionMode = UiSessionModes.Exclusive }
	];

	public async Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(request);

		var surface = request.Surface;

		foreach (var kind in Enum.GetValues<FeedWidgetKind>())
		{
			if (UiWidgetSurface.IsConfigurationFor(surface, FeedWidgetTypes.QualifiedId(kind)))
			{
				return new UiViewSession(new UiView(surface,
					FeedWidgetConfigView.Build(kind, UiWidgetSurface.Data(surface), _source.Feeds)));
			}
		}

		if (surface.Kind == UiSurfaceKinds.Dialog)
		{
			return surface.Attributes.TryGetValue(UiDialogSurfaceAttributes.ViewId, out var viewId)
				&& viewId.ValueKind == JsonValueKind.String
				&& viewId.GetString() == FeedWidgetTypes.DialogViewId
					? FeedArticleDialog.Create(surface, _source, _images, _time)
					: null;
		}

		FeedWidgetKind? requested = UiWidgetSurface.IsFor(surface, FeedWidgetTypes.ListQualifiedId)
			? FeedWidgetKind.List
			: UiWidgetSurface.IsFor(surface, FeedWidgetTypes.FeaturedQualifiedId)
				? FeedWidgetKind.Featured
				: null;
		if (requested is not { } widgetKind)
		{
			return null;
		}

		var data = UiWidgetSurface.Data(surface);
		var options = FeedWidgetOptions.Parse(widgetKind, data);
		var registry = _resources();
		var icon = await IconAsync(registry, cancellationToken).ConfigureAwait(false);

		if (UiWidgetSurface.IsSampleRequested(surface))
		{
			return new UiViewSession(new UiView(surface,
				FeedWidgetView.Build(widgetKind,
					new UiState<FeedWidgetState>(Sample(UiWidgetSurface.Culture(surface))),
					new UiState<int>(0),
					new UiState<UiResource?>(icon),
					options with { ShowThumbnail = false },
					_ => UiEventOutcome.Accepted,
					UiWidgetSafeArea.RadiusOf(surface),
					FeedWidgetOptions.BackgroundColor(data))));
		}

		return new FeedWidgetSession(surface,
			widgetKind,
			options,
			_source,
			_images,
			_time,
			icon,
			registry is null ? () => IconAsync(_resources(), CancellationToken.None) : null);
	}

	internal static FeedWidgetState Sample(string? culture)
	{
		var feed = WidgetText.Resolve(Strings.Sample.Feed(), culture);
		var description = WidgetText.Resolve(Strings.Sample.Description(), culture);
		var entry = Guid.Empty;

		return new FeedWidgetState(FeedWidgetStatus.Ok,
			feed,
			[
				new FeedRow("sample-1", entry, "1", WidgetText.Resolve(Strings.Sample.First(), culture), feed)
				{
					Description = description,
					Unread = true
				},
				new FeedRow("sample-2", entry, "2", WidgetText.Resolve(Strings.Sample.Second(), culture), feed)
				{
					Description = description
				},
				new FeedRow("sample-3", entry, "3", WidgetText.Resolve(Strings.Sample.Third(), culture), feed)
				{
					Description = description
				}
			]);
	}

	internal static async Task<UiResource?> IconAsync(
		IUiResourceRegistry? resources,
		CancellationToken cancellationToken)
	{
		if (resources is null)
		{
			return null;
		}

		try
		{
			return await resources.GetPluginIconAsync(IconFolder, Icon, cancellationToken).ConfigureAwait(false);
		}
		catch (UiResourceException)
		{
			return null;
		}
	}
}
