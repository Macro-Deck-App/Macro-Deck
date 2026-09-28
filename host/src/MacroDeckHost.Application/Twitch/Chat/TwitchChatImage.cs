using System.Security.Cryptography;
using System.Text;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Application.Twitch.Chat;

public enum TwitchChatImageKind
{
	Emote,

	Badge
}

public sealed record TwitchChatImage(TwitchChatImageKind Kind, string Id, string Url)
{
	public string Key => Kind == TwitchChatImageKind.Emote ? "emote:" + Id : "badge:" + Id;

	// Badge ids are hashed so the resource name stays short and free of characters a resource id refuses.
	public string ResourceName => Kind == TwitchChatImageKind.Emote ? "emote-" + Id : "badge-" + ShortHash(Id);

	public static TwitchChatImage Emote(string emoteId)
		=> new(TwitchChatImageKind.Emote,
			emoteId,
			$"https://static-cdn.jtvnw.net/emoticons/v2/{emoteId}/default/dark/2.0");

	public static TwitchChatImage Badge(string setId, string id, string url)
		=> new(TwitchChatImageKind.Badge, setId + "/" + id, url);

	private static string ShortHash(string value)
		=> Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
}

public interface ITwitchChatImages
{
	event EventHandler<TwitchChatImage>? Resolved;

	UiResource? Find(TwitchChatImage image);

	void Request(TwitchChatImage image);

	void Pin(IReadOnlyCollection<TwitchChatImage> referenced);
}
