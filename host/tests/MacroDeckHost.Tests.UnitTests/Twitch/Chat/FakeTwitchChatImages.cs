using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Tests.UnitTests.Twitch.Chat;

internal sealed class FakeTwitchChatImages : ITwitchChatImages
{
	private readonly Dictionary<string, UiResource> _resolved = new(StringComparer.Ordinal);
	private readonly IUiResourceStore? _store;

	public FakeTwitchChatImages(IUiResourceStore? store = null)
	{
		_store = store;
	}

	public event EventHandler<TwitchChatImage>? Resolved;

	public List<TwitchChatImage> Requested { get; } = [];

	public IReadOnlySet<string> Pinned { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

	public UiResource? Find(TwitchChatImage image) => _resolved.GetValueOrDefault(image.Key);

	public void Request(TwitchChatImage image) => Requested.Add(image);

	public void Pin(IReadOnlyCollection<TwitchChatImage> referenced)
		=> Pinned = referenced.Select(image => image.Key).ToHashSet(StringComparer.Ordinal);

	public UiResource Resolve(TwitchChatImage image)
	{
		var resource = _store?.Register(new UiResourceRegistration
			{
				OwnerId = TwitchChatWidgetType.OwnerId,
				Name = image.ResourceName,
				MediaType = "image/png",
				Content = new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3, 4 },
			}) ??
			new UiResource
			{
				ResourceId = TwitchChatWidgetType.OwnerId + "." + image.ResourceName,
				ContentHash = "sha256:" + new string('a', 64),
			};

		_resolved[image.Key] = resource;
		Resolved?.Invoke(this, image);
		return resource;
	}
}
