using System.Text.Json;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Widgets;

public enum WidgetTypeFavoriteOutcome
{
	Updated,
	Unchanged,
	UnknownType,
	LimitReached
}

public sealed record WidgetTypeFavoriteResult(WidgetTypeFavoriteOutcome Outcome, IReadOnlyList<string> TypeIds);

public interface IWidgetTypeFavoritesService
{
	Task<IReadOnlyList<string>> GetFavorites(CancellationToken cancellationToken = default);

	Task<WidgetTypeFavoriteResult> SetFavorite(string widgetTypeId, bool favorite, CancellationToken cancellationToken = default);
}

public sealed class WidgetTypeFavoritesService : IWidgetTypeFavoritesService, IDisposable
{
	public const int MaxFavorites = 256;

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IPublisher _publisher;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public WidgetTypeFavoritesService(IServiceScopeFactory scopeFactory,
		IWidgetTypeRegistry widgetTypes,
		IPublisher publisher)
	{
		_scopeFactory = scopeFactory;
		_widgetTypes = widgetTypes;
		_publisher = publisher;
	}

	public async Task<IReadOnlyList<string>> GetFavorites(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			return await Read();
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<WidgetTypeFavoriteResult> SetFavorite(string widgetTypeId,
		bool favorite,
		CancellationToken cancellationToken = default)
	{
		List<string> updated;

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var current = await Read();
			var resolved = _widgetTypes.Resolve(widgetTypeId);

			if (favorite)
			{
				if (resolved is null)
				{
					return new WidgetTypeFavoriteResult(WidgetTypeFavoriteOutcome.UnknownType, current);
				}

				if (current.Contains(resolved, StringComparer.Ordinal))
				{
					return new WidgetTypeFavoriteResult(WidgetTypeFavoriteOutcome.Unchanged, current);
				}

				if (current.Count >= MaxFavorites)
				{
					return new WidgetTypeFavoriteResult(WidgetTypeFavoriteOutcome.LimitReached, current);
				}

				updated = [.. current, resolved];
			}
			else
			{
				var stored = resolved ?? widgetTypeId;
				if (!current.Contains(stored, StringComparer.Ordinal))
				{
					return new WidgetTypeFavoriteResult(WidgetTypeFavoriteOutcome.Unchanged, current);
				}

				updated = [.. current.Where(id => !string.Equals(id, stored, StringComparison.Ordinal))];
			}

			await Write(updated);

			// Published under the gate so two quick toggles can never broadcast out of order.
			await _publisher.Publish(new WidgetTypeFavoritesChangedNotification(updated), cancellationToken);
		}
		finally
		{
			_gate.Release();
		}

		return new WidgetTypeFavoriteResult(WidgetTypeFavoriteOutcome.Updated, updated);
	}

	public void Dispose() => _gate.Dispose();

	private async Task<List<string>> Read()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var value = (await preferences.GetByKey(AppPreferenceService.WidgetTypeFavoritesKey))?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			return [];
		}

		try
		{
			var ids = JsonSerializer.Deserialize<List<string?>>(value) ?? [];
			return [.. ids.OfType<string>().Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxFavorites)];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	private async Task Write(IReadOnlyList<string> ids)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await preferences.SetValue(AppPreferenceService.WidgetTypeFavoritesKey, JsonSerializer.Serialize(ids));
	}
}
