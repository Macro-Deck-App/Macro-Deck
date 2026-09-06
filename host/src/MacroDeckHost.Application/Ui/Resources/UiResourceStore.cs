using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Ui.Model.Identity;
using MacroDeck.Ui.Model.Resources;

namespace MacroDeckHost.Application.Ui.Resources;

public sealed class UiResourceStore : IUiResourceStore
{
	private readonly ConcurrentDictionary<string, UiResourceContent> _resources =
		new(StringComparer.Ordinal);

	public UiResource Register(UiResourceRegistration registration)
	{
		ArgumentNullException.ThrowIfNull(registration);

		var resourceId = $"{registration.OwnerId}.{registration.Name}";

		if (!UiIdentifier.TryValidate(resourceId, out var error))
		{
			throw new ArgumentException($"'{resourceId}' is not a usable resource id. {error}",
				nameof(registration));
		}

		if (registration.Content.Length > ProtocolLimits.MaxUiResourceBytes)
		{
			throw new ArgumentException($"The resource is {registration.Content.Length} bytes, over the " +
				$"{ProtocolLimits.MaxUiResourceBytes} byte limit.",
				nameof(registration));
		}

		var content = new UiResourceContent
		{
			Content = registration.Content,
			MediaType = registration.MediaType,
			ContentHash = AssetContentHash.Compute(registration.Content.Span),
		};

		_resources[resourceId] = content;

		return new UiResource
		{
			ResourceId = resourceId,
			ContentHash = content.ContentHash,
			MediaType = content.MediaType,
			ByteLength = registration.Content.Length,
		};
	}

	public bool TryGet(string resourceId, out UiResourceContent content)
	{
		if (!string.IsNullOrEmpty(resourceId) && _resources.TryGetValue(resourceId, out var found))
		{
			content = found;

			return true;
		}

		content = null!;

		return false;
	}
}
