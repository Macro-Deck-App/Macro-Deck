using System.Text;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Twitch.Chat;

namespace MacroDeckHost.Widgets.TwitchChat;

internal enum TwitchChatStatus
{
	Offline,

	Empty,

	Messages
}

internal sealed record TwitchChatLine(
	string Key,
	string FallbackKey,
	string Text,
	IReadOnlyList<UiTextSpan> Spans,
	int ResolvedImages,
	int Bytes,
	int FallbackBytes);

internal sealed record TwitchChatViewState(
	TwitchChatStatus Status,
	IReadOnlyList<TwitchChatLine> Lines,
	IReadOnlyList<TwitchChatLine> FallbackLines,
	string? AccountName = null)
{
	public static TwitchChatViewState Offline { get; } = new(TwitchChatStatus.Offline, [], []);

	public static TwitchChatViewState Empty { get; } = new(TwitchChatStatus.Empty, [], []);
}

internal sealed record TwitchChatLineLayout(
	Func<TwitchChatLine, UiElement> Message,
	Func<TwitchChatLine, UiElement> FallbackMessage,
	int MaxMessages,
	int MaxBytes,
	int FallbackMessages);

internal sealed class TwitchChatLines
{
	public const int MaxMessages = 50;
	public const int MaxBytes = 32 * 1024;
	public const int MaxEmoteImages = 20;
	public const int MaxBadges = 3;

	private const string BadgeGap = "\u202F";
	public const int FallbackMessages = 3;

	// A measured node carries a short standalone id; its id inside the real tree is at most this much longer.
	private const int IdAllowance = 128;

	private static readonly UiSurface _measureSurface = new()
	{
		Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared,
	};

	public static readonly TwitchChatLineLayout Widget = WidgetLayout(1);

	public static TwitchChatLineLayout WidgetLayout(double scale)
		=> new(line => TwitchChatWidgetView.Message(line, scale),
			line => TwitchChatWidgetView.FallbackMessage(line, scale),
			MaxMessages,
			MaxBytes,
			FallbackMessages);

	private readonly string _separator;
	private readonly ITwitchChatImages? _images;
	private readonly TwitchChatLineLayout _layout;

	private Dictionary<string, (TwitchChatMessage Message, TwitchChatLine Line)> _cache
		= new(StringComparer.Ordinal);

	public TwitchChatLines(string separator, ITwitchChatImages? images, TwitchChatLineLayout? layout = null)
	{
		_separator = separator;
		_images = images;
		_layout = layout ?? Widget;
	}

	public TwitchChatViewState Build(TwitchChatSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		if (snapshot.Account is null || !snapshot.IsConnected)
		{
			_cache = new Dictionary<string, (TwitchChatMessage, TwitchChatLine)>(StringComparer.Ordinal);
			return TwitchChatViewState.Offline with { AccountName = snapshot.Account?.Label };
		}

		if (snapshot.Messages.Count == 0)
		{
			_cache = new Dictionary<string, (TwitchChatMessage, TwitchChatLine)>(StringComparer.Ordinal);
			return TwitchChatViewState.Empty with { AccountName = snapshot.Account.Label };
		}

		var next = new Dictionary<string, (TwitchChatMessage, TwitchChatLine)>(StringComparer.Ordinal);
		var kept = new List<TwitchChatLine>();
		var total = 0;

		for (var index = snapshot.Messages.Count - 1; index >= 0 && kept.Count < _layout.MaxMessages; index--)
		{
			var message = snapshot.Messages[index];
			var line = LineFor(message);
			var cost = line.Bytes + (kept.Count < _layout.FallbackMessages ? line.FallbackBytes : 0);

			if (total + cost > _layout.MaxBytes)
			{
				break;
			}

			total += cost;
			kept.Add(line);
			next[message.MessageId] = (message, line);
		}

		_cache = next;
		kept.Reverse();

		return new TwitchChatViewState(TwitchChatStatus.Messages,
			kept,
			[.. kept.TakeLast(_layout.FallbackMessages)],
			snapshot.Account.Label);
	}

	public static TwitchChatLine Build(
		TwitchChatMessage message,
		string separator,
		ITwitchChatImages? images,
		TwitchChatLineLayout? layout = null)
	{
		ArgumentNullException.ThrowIfNull(message);

		layout ??= Widget;

		var spans = new List<UiTextSpan>();
		var resolved = 0;

		foreach (var badge in message.Badges.Take(MaxBadges))
		{
			if (badge.ImageUrl is { } url &&
				Find(images, TwitchChatImage.Badge(badge.SetId, badge.Id, url)) is { } image)
			{
				if (resolved > 0)
				{
					spans.Add(UiTextSpan.FromText(BadgeGap));
				}

				spans.Add(UiTextSpan.FromImage(image));
				resolved++;
			}
		}

		if (resolved > 0)
		{
			spans.Add(UiTextSpan.FromText(" "));
		}

		spans.Add(UiTextSpan.FromText(message.ChatterName, message.Color, UiComponentTextWeights.SemiBold));
		spans.Add(UiTextSpan.FromText(separator));

		var text = new StringBuilder();
		var emotes = 0;

		foreach (var fragment in message.Fragments)
		{
			if (fragment is { Kind: TwitchChatFragmentKind.Emote, EmoteId: { } emoteId } &&
				emotes < MaxEmoteImages &&
				Find(images, TwitchChatImage.Emote(emoteId)) is { } emote)
			{
				Flush(spans, text);
				spans.Add(UiTextSpan.FromImage(emote, fragment.Text));
				emotes++;
				resolved++;
				continue;
			}

			text.Append(fragment.Text);
		}

		Flush(spans, text);

		var key = TwitchChatStyle.MessageKey(message.MessageId);
		var line = new TwitchChatLine(key,
			"f" + key[1..],
			message.ChatterName + separator + message.PlainText,
			spans,
			resolved,
			0,
			0);

		return line with
		{
			Bytes = Measure(layout.Message(line)),
			FallbackBytes = Measure(layout.FallbackMessage(line)),
		};
	}

	private TwitchChatLine LineFor(TwitchChatMessage message)
	{
		if (_cache.TryGetValue(message.MessageId, out var cached) &&
			ReferenceEquals(cached.Message, message) &&
			cached.Line.ResolvedImages == CountResolved(message))
		{
			return cached.Line;
		}

		return Build(message, _separator, _images, _layout);
	}

	private int CountResolved(TwitchChatMessage message)
	{
		if (_images is null)
		{
			return 0;
		}

		var badges = message.Badges.Take(MaxBadges)
			.Count(badge => badge.ImageUrl is { } url &&
				_images.Find(TwitchChatImage.Badge(badge.SetId, badge.Id, url)) is not null);

		var emotes = message.Fragments
			.Where(fragment => fragment is { Kind: TwitchChatFragmentKind.Emote, EmoteId: not null })
			.Count(fragment => _images.Find(TwitchChatImage.Emote(fragment.EmoteId!)) is not null);

		return badges + Math.Min(emotes, MaxEmoteImages);
	}

	private static UiResource? Find(ITwitchChatImages? images, TwitchChatImage image)
		=> images?.Find(image) is { } resource
			? new UiResource { ResourceId = resource.ResourceId, ContentHash = resource.ContentHash }
			: null;

	private static void Flush(List<UiTextSpan> spans, StringBuilder text)
	{
		if (text.Length == 0)
		{
			return;
		}

		spans.Add(UiTextSpan.FromText(text.ToString()));
		text.Clear();
	}

	private static int Measure(UiElement element)
		=> UiCanonicalJson.SerializeToUtf8Bytes(UiViewBuilder.Build(_measureSurface, element).Root).Length +
			IdAllowance;
}
