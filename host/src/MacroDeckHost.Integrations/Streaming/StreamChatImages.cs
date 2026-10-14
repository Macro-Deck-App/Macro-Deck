using System.Security.Cryptography;
using System.Text;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Integrations.Streaming;

public enum StreamChatImageKind
{
	Emote,

	Badge
}

public sealed record StreamChatImage(StreamChatImageKind Kind, string Id, string? Url = null)
{
	public const int MaxResourceNameLength = 64;

	public string Key => Kind == StreamChatImageKind.Emote ? "emote:" + Id : "badge:" + Id;

	// Badge ids, and an emote id too long for a resource name, are hashed so the name stays short and free of
	// characters a resource name refuses.
	public string ResourceName
		=> Kind == StreamChatImageKind.Emote && ("emote-" + Id).Length <= MaxResourceNameLength
			? "emote-" + Id
			: (Kind == StreamChatImageKind.Emote ? "emote-" : "badge-") + ShortHash(Id);

	public static StreamChatImage Emote(string emoteId)
	{
		return new StreamChatImage(StreamChatImageKind.Emote, emoteId);
	}

	public static StreamChatImage Badge(string setId, string id, string url)
	{
		return new StreamChatImage(StreamChatImageKind.Badge, setId + "/" + id, url);
	}

	public static string ShortHash(string value)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..16];
	}
}

public interface IStreamChatImages
{
	event EventHandler<StreamChatImage>? Resolved;

	UiResource? Find(StreamChatImage image);

	void Request(StreamChatImage image);

	void Pin(IReadOnlyCollection<StreamChatImage> referenced);
}
