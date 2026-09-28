using System.Globalization;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Announcements;

public sealed class AnnouncementService : IAnnouncementService, IDisposable
{
	public static readonly TimeSpan FirstRunWindow = TimeSpan.FromDays(14);

	private readonly IPlatformAnnouncementClient _client;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _gate = new(1, 1);

	private Announcement? _pending;
	private int _highestFetched;

	public AnnouncementService(IPlatformAnnouncementClient client,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider)
	{
		_client = client;
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
	}

	public event Action? Changed;

	public Announcement? Pending => Volatile.Read(ref _pending);

	public async Task Refresh(CancellationToken cancellationToken)
	{
		var fetch = await _client.GetLatest(cancellationToken);
		if (fetch is AnnouncementFetch.Failed)
		{
			return;
		}

		await _gate.WaitAsync(cancellationToken);
		try
		{
			var previous = _pending;
			_pending = await Decide(fetch, cancellationToken);
			if (previous != _pending)
			{
				Changed?.Invoke();
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task MarkSeen(int number, CancellationToken cancellationToken)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			if (number <= 0 || number > _highestFetched)
			{
				return;
			}

			var stored = await ReadLastSeen();
			if (stored is null || number > stored)
			{
				await WriteLastSeen(number);
			}

			if (_pending is { } pending && pending.Number <= number)
			{
				_pending = null;
				Changed?.Invoke();
			}
		}
		finally
		{
			_gate.Release();
		}
	}

	private async Task<Announcement?> Decide(AnnouncementFetch fetch, CancellationToken cancellationToken)
	{
		var stored = await ReadLastSeen();
		if (fetch is not AnnouncementFetch.Published { Announcement: var latest })
		{
			if (stored is null)
			{
				await WriteLastSeen(0);
			}

			return null;
		}

		_highestFetched = Math.Max(_highestFetched, latest.Number);

		if (stored is null)
		{
			var recent = _timeProvider.GetUtcNow() - latest.PublishedAt <= FirstRunWindow;
			await WriteLastSeen(recent ? 0 : latest.Number);
			return recent ? latest : null;
		}

		return latest.Number > stored ? latest : null;
	}

	private async Task<int?> ReadLastSeen()
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		var value = (await preferences.GetByKey(AppPreferenceService.AnnouncementLastSeenNumberKey))?.Value;

		return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
	}

	private async Task WriteLastSeen(int number)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceRepository>();
		await preferences.SetValue(AppPreferenceService.AnnouncementLastSeenNumberKey,
			number.ToString(CultureInfo.InvariantCulture));
	}

	public void Dispose() => _gate.Dispose();
}
