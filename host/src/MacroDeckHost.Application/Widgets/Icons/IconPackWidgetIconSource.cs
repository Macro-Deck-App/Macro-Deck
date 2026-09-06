using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Widgets.Icons;

/// <summary>
/// The built-in icon pack catalog as a <see cref="IWidgetIconSource" /> - the only place a widget icon
/// reference is ever parsed as a GUID. <see cref="IIconPackCache" /> is a singleton and answered
/// synchronously, but <see cref="IIconService" /> sits behind a scoped mediator, so a scope is opened per
/// resolve exactly as <c>WidgetIconResources</c> used to do directly.
/// </summary>
public sealed class IconPackWidgetIconSource : IWidgetIconSource
{
	private readonly IIconPackCache _iconPackCache;
	private readonly IServiceScopeFactory _scopeFactory;

	public IconPackWidgetIconSource(IIconPackCache iconPackCache, IServiceScopeFactory scopeFactory)
	{
		_iconPackCache = iconPackCache;
		_scopeFactory = scopeFactory;
	}

	public string Type => WidgetIconReference.IconPackType;

	public string? GetVersion(string reference)
	{
		if (!Guid.TryParse(reference, out var id) || _iconPackCache.GetIconById(id) is not { } icon)
		{
			return null;
		}

		return icon.MasterContentHash ?? icon.SourceContentHash;
	}

	public async Task<WidgetIconImage?> GetImageAsync(
		string reference,
		int size,
		bool acceptWebp,
		bool staticFrame,
		CancellationToken cancellationToken)
	{
		if (!Guid.TryParse(reference, out var id))
		{
			return null;
		}

		using var scope = _scopeFactory.CreateScope();
		var iconService = scope.ServiceProvider.GetRequiredService<IIconService>();

		var result = await iconService.GetImage(id, size, acceptWebp, staticFrame, cancellationToken)
			.ConfigureAwait(false);
		if (!result.Success)
		{
			return null;
		}

		return new WidgetIconImage(result.Data!.Content, result.Data.ContentType);
	}
}
