using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Infrastructure.Integrations;

public sealed class BuiltInUiResourceRegistry : IUiResourceRegistry
{
	// Built-ins ship their own vector logo, which a plugin may not upload.
	private const string SvgMediaType = "image/svg+xml";

	private readonly string _ownerId;
	private readonly IUiResourceStore _store;

	public BuiltInUiResourceRegistry(string ownerId, IUiResourceStore store)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
		ArgumentNullException.ThrowIfNull(store);

		_ownerId = ownerId;
		_store = store;
	}

	public Task<UiResource> RegisterAsync(
		string name,
		ReadOnlyMemory<byte> content,
		string mediaType,
		CancellationToken cancellationToken = default)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			throw new ArgumentException("The resource name is not valid.", nameof(name));
		}

		if (!UiResourceRules.IsSupportedMediaType(mediaType) &&
			!string.Equals(mediaType, SvgMediaType, StringComparison.OrdinalIgnoreCase))
		{
			throw new ArgumentException("The media type is not supported.", nameof(mediaType));
		}

		if (content.IsEmpty)
		{
			throw new ArgumentException("The resource is empty.", nameof(content));
		}

		try
		{
			return Task.FromResult(_store.Register(new UiResourceRegistration
			{
				OwnerId = _ownerId,
				Name = name,
				MediaType = mediaType.ToLowerInvariant(),
				Content = content.ToArray(),
			}));
		}
		catch (ArgumentException exception)
		{
			throw new UiResourceException(UiResourceErrorCode.Failed, exception.Message, exception);
		}
	}

	public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
	{
		if (!UiResourceRules.IsValidName(name))
		{
			throw new ArgumentException("The resource name is not valid.", nameof(name));
		}

		_store.Remove($"{_ownerId}.{name}");

		return Task.CompletedTask;
	}
}
