using System.Text.Json;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.ColorPalette;

public enum ColorPaletteOutcome
{
	Updated,
	Unchanged,
	InvalidColor,
	LimitReached
}

public sealed record ColorPaletteResult(ColorPaletteOutcome Outcome, IReadOnlyList<string> Colors);

public interface IColorPaletteService
{
	Task<IReadOnlyList<string>> GetColors(CancellationToken cancellationToken = default);

	Task<ColorPaletteResult> SetColor(string color, bool present, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<string>> RestoreDefaults(CancellationToken cancellationToken = default);
}

public sealed class ColorPaletteService : IColorPaletteService, IDisposable
{
	public const int MaxColors = 24;

	public static readonly IReadOnlyList<string> DefaultColors =
		["#ef4444", "#f59e0b", "#eab308", "#22c55e", "#3b82f6", "#8b5cf6", "#ec4899"];

	private readonly IServiceScopeFactory _scopeFactory;
	private readonly IPublisher _publisher;
	private readonly SemaphoreSlim _gate = new(1, 1);

	public ColorPaletteService(IServiceScopeFactory scopeFactory, IPublisher publisher)
	{
		_scopeFactory = scopeFactory;
		_publisher = publisher;
	}

	public async Task<IReadOnlyList<string>> GetColors(CancellationToken cancellationToken = default)
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

	public async Task<ColorPaletteResult> SetColor(string color,
		bool present,
		CancellationToken cancellationToken = default)
	{
		List<string> updated;

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var current = await Read();
			var normalized = Normalize(color);
			if (normalized is null)
			{
				return new ColorPaletteResult(ColorPaletteOutcome.InvalidColor, current);
			}

			var contained = current.Contains(normalized, StringComparer.Ordinal);
			if (contained == present)
			{
				return new ColorPaletteResult(ColorPaletteOutcome.Unchanged, current);
			}

			if (present && current.Count >= MaxColors)
			{
				return new ColorPaletteResult(ColorPaletteOutcome.LimitReached, current);
			}

			updated = present
				? [.. current, normalized]
				: [.. current.Where(existing => !string.Equals(existing, normalized, StringComparison.Ordinal))];

			await Write(updated);

			// Published under the gate so two quick changes can never broadcast out of order.
			await _publisher.Publish(new ColorPaletteChangedNotification(updated), cancellationToken);
		}
		finally
		{
			_gate.Release();
		}

		return new ColorPaletteResult(ColorPaletteOutcome.Updated, updated);
	}

	public async Task<IReadOnlyList<string>> RestoreDefaults(CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
			await preferences.SetValue(AppPreferenceService.ColorPaletteKey, string.Empty);
			await _publisher.Publish(new ColorPaletteChangedNotification(DefaultColors), cancellationToken);
			return DefaultColors;
		}
		finally
		{
			_gate.Release();
		}
	}

	public void Dispose() => _gate.Dispose();

	public static string? Normalize(string? color)
	{
		if (string.IsNullOrWhiteSpace(color))
		{
			return null;
		}

		var text = color.Trim();
		if (text[0] != '#')
		{
			return null;
		}

		var digits = text[1..].ToLowerInvariant();
		if (!digits.All(Uri.IsHexDigit))
		{
			return null;
		}

		digits = digits.Length switch
		{
			3 => string.Concat(digits.Select(digit => new string(digit, 2))),
			6 => digits,
			8 => digits.EndsWith("ff", StringComparison.Ordinal) ? digits[..6] : digits,
			_ => string.Empty
		};

		return digits.Length == 0 ? null : $"#{digits}";
	}

	private async Task<List<string>> Read()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var value = (await preferences.GetByKey(AppPreferenceService.ColorPaletteKey))?.Value;
		if (string.IsNullOrWhiteSpace(value))
		{
			return [.. DefaultColors];
		}

		try
		{
			var colors = JsonSerializer.Deserialize<List<string?>>(value) ?? [];
			return [.. colors.Select(Normalize).OfType<string>().Distinct(StringComparer.Ordinal).Take(MaxColors)];
		}
		catch (JsonException)
		{
			return [.. DefaultColors];
		}
	}

	private async Task Write(IReadOnlyList<string> colors)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await preferences.SetValue(AppPreferenceService.ColorPaletteKey, JsonSerializer.Serialize(colors));
	}
}
