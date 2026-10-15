using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Streaming;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Integrations.Twitch.Widgets;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

internal sealed class FakeTwitchChatImages : IStreamChatImages
{
	private readonly Dictionary<string, UiResource> _resolved = new(StringComparer.Ordinal);
	private readonly IUiResourceStore? _store;

	public FakeTwitchChatImages(IUiResourceStore? store = null)
	{
		_store = store;
	}

	public event EventHandler<StreamChatImage>? Resolved;

	public List<StreamChatImage> Requested { get; } = [];

	public IReadOnlySet<string> Pinned { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

	public UiResource? Find(StreamChatImage image)
	{
		return _resolved.GetValueOrDefault(image.Key);
	}

	public void Request(StreamChatImage image)
	{
		Requested.Add(image);
	}

	public void Pin(IReadOnlyCollection<StreamChatImage> referenced)
	{
		Pinned = referenced.Select(image => image.Key).ToHashSet(StringComparer.Ordinal);
	}

	public UiResource Resolve(StreamChatImage image)
	{
		var resource = _store?.Register(new UiResourceRegistration
			{
				OwnerId = TwitchStreamPlatform.Platform.OwnerId,
				Name = image.ResourceName,
				MediaType = "image/png",
				Content = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 }
			})
			?? new UiResource
			{
				ResourceId = TwitchStreamPlatform.Platform.OwnerId + "." + image.ResourceName,
				ContentHash = "sha256:" + new string('a', 64)
			};

		_resolved[image.Key] = resource;
		Resolved?.Invoke(this, image);
		return resource;
	}
}
