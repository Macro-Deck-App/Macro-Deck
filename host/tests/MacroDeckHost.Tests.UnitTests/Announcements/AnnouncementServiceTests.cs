using System.Collections.Concurrent;
using MacroDeckHost.Application.Announcements;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Announcements;

[TestFixture]
internal sealed class AnnouncementServiceTests
{
	private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

	private ScriptedClient _client = null!;
	private MemoryPreferences _preferences = null!;
	private ServiceProvider _services = null!;
	private AnnouncementService _announcements = null!;
	private int _changes;

	[SetUp]
	public void SetUp()
	{
		_changes = 0;
		_client = new ScriptedClient();
		_preferences = new MemoryPreferences();
		_services = new ServiceCollection()
			.AddScoped<IAppPreferenceRepository>(_ => _preferences)
			.BuildServiceProvider();
		_announcements = new AnnouncementService(_client,
			_services.GetRequiredService<IServiceScopeFactory>(),
			new ManualTimeProvider { Now = Now });
		_announcements.Changed += () => _changes++;
	}

	[TearDown]
	public void TearDown()
	{
		_announcements.Dispose();
		_services.Dispose();
	}

	[Test]
	public async Task A_first_start_shows_an_announcement_published_within_fourteen_days()
	{
		_client.Next = Published(4, Now.AddDays(-13));

		await _announcements.Refresh(default);

		Assert.Multiple(() =>
		{
			Assert.That(_announcements.Pending?.Number, Is.EqualTo(4));
			Assert.That(LastSeen, Is.EqualTo("0"));
			Assert.That(_changes, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_first_start_that_decided_to_show_an_announcement_still_shows_it_on_the_next_start()
	{
		_client.Next = Published(4, Now.AddDays(-13));
		await _announcements.Refresh(default);

		using var restarted = new AnnouncementService(_client,
			_services.GetRequiredService<IServiceScopeFactory>(),
			new ManualTimeProvider { Now = Now.AddDays(30) });
		await restarted.Refresh(default);

		Assert.That(restarted.Pending?.Number, Is.EqualTo(4));
	}

	[Test]
	public async Task An_installation_that_has_checked_before_shows_an_announcement_of_any_age()
	{
		_client.Next = new AnnouncementFetch.NonePublished();
		await _announcements.Refresh(default);

		_client.Next = Published(1, Now.AddDays(-15));
		await _announcements.Refresh(default);

		Assert.That(_announcements.Pending?.Number, Is.EqualTo(1));
	}

	[Test]
	public async Task A_first_start_stores_an_old_announcement_without_showing_it()
	{
		_client.Next = Published(4, Now.AddDays(-15));

		await _announcements.Refresh(default);

		Assert.Multiple(() =>
		{
			Assert.That(_announcements.Pending, Is.Null);
			Assert.That(LastSeen, Is.EqualTo("4"));
			Assert.That(_changes, Is.Zero);
		});

		_client.Next = Published(5, Now.AddDays(-30));
		await _announcements.Refresh(default);

		Assert.That(_announcements.Pending?.Number, Is.EqualTo(5), "a newer number is news even when it is old");
	}

	[Test]
	public async Task A_higher_number_is_shown_even_after_a_gap()
	{
		LastSeen = "3";
		_client.Next = Published(7, Now.AddDays(-100));

		await _announcements.Refresh(default);

		Assert.That(_announcements.Pending?.Number, Is.EqualTo(7));
	}

	[Test]
	public async Task A_seen_announcement_is_never_shown_again_even_after_an_edit()
	{
		_client.Next = Published(4, Now.AddDays(-1));
		await _announcements.Refresh(default);
		await _announcements.MarkSeen(4, default);

		_client.Next = Published(4, Now.AddDays(-1), "Edited", Now);
		await _announcements.Refresh(default);

		Assert.Multiple(() =>
		{
			Assert.That(_announcements.Pending, Is.Null);
			Assert.That(LastSeen, Is.EqualTo("4"));
		});
	}

	[Test]
	public async Task An_edit_of_the_pending_announcement_updates_its_text_and_keeps_its_number()
	{
		LastSeen = "3";
		_client.Next = Published(4, Now.AddDays(-1));
		await _announcements.Refresh(default);

		_client.Next = Published(4, Now.AddDays(-1), "Edited", Now);
		await _announcements.Refresh(default);

		Assert.Multiple(() =>
		{
			Assert.That(_announcements.Pending?.Number, Is.EqualTo(4));
			Assert.That(_announcements.Pending?.Content, Is.EqualTo("Edited"));
			Assert.That(_announcements.Pending?.UpdatedAt, Is.EqualTo(Now));
			Assert.That(_changes, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task A_withdrawn_announcement_is_no_longer_pending_and_the_stored_number_stays()
	{
		LastSeen = "5";
		_client.Next = Published(7, Now);
		await _announcements.Refresh(default);

		_client.Next = new AnnouncementFetch.NonePublished();
		await _announcements.Refresh(default);
		Assert.That(_announcements.Pending, Is.Null);

		_client.Next = Published(7, Now);
		await _announcements.Refresh(default);
		_client.Next = Published(6, Now);
		await _announcements.Refresh(default);
		Assert.That(_announcements.Pending?.Number, Is.EqualTo(6), "the older one is still unseen");

		_client.Next = Published(4, Now);
		await _announcements.Refresh(default);
		Assert.Multiple(() =>
		{
			Assert.That(_announcements.Pending, Is.Null);
			Assert.That(LastSeen, Is.EqualTo("5"));
		});
	}

	[Test]
	public async Task A_failed_check_changes_nothing()
	{
		LastSeen = "3";
		_client.Next = Published(4, Now);
		await _announcements.Refresh(default);

		_client.Next = new AnnouncementFetch.Failed();
		await _announcements.Refresh(default);

		Assert.Multiple(() =>
		{
			Assert.That(_announcements.Pending?.Number, Is.EqualTo(4));
			Assert.That(_changes, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Marking_seen_never_lowers_the_stored_number()
	{
		LastSeen = "3";
		_client.Next = Published(9, Now);
		await _announcements.Refresh(default);
		await _announcements.MarkSeen(9, default);

		await _announcements.MarkSeen(4, default);

		Assert.That(LastSeen, Is.EqualTo("9"));
	}

	[Test]
	public async Task Marking_a_number_the_host_never_fetched_as_seen_is_ignored()
	{
		LastSeen = "3";
		_client.Next = Published(4, Now);
		await _announcements.Refresh(default);

		await _announcements.MarkSeen(int.MaxValue, default);

		Assert.Multiple(() =>
		{
			Assert.That(LastSeen, Is.EqualTo("3"));
			Assert.That(_announcements.Pending?.Number, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task Concurrent_seen_marks_keep_the_highest_number()
	{
		LastSeen = "1";
		_client.Next = Published(5, Now);
		await _announcements.Refresh(default);

		await Task.WhenAll(Enumerable.Range(1, 5).Select(number => Task.Run(() => _announcements.MarkSeen(number, default))));

		Assert.Multiple(() =>
		{
			Assert.That(LastSeen, Is.EqualTo("5"));
			Assert.That(_announcements.Pending, Is.Null);
		});
	}

	private string? LastSeen
	{
		get => _preferences.Values.GetValueOrDefault(AppPreferenceService.AnnouncementLastSeenNumberKey);
		set => _preferences.Values[AppPreferenceService.AnnouncementLastSeenNumberKey] = value!;
	}

	private static AnnouncementFetch.Published Published(int number,
		DateTimeOffset publishedAt,
		string content = "Body",
		DateTimeOffset? updatedAt = null)
		=> new AnnouncementFetch.Published(new Announcement(number,
			$"Announcement {number}",
			content,
			publishedAt,
			updatedAt ?? publishedAt));

	private sealed class ScriptedClient : IPlatformAnnouncementClient
	{
		public AnnouncementFetch Next { get; set; } = new AnnouncementFetch.NonePublished();

		public Task<AnnouncementFetch> GetLatest(CancellationToken cancellationToken) => Task.FromResult(Next);
	}

	private sealed class MemoryPreferences : IAppPreferenceRepository
	{
		public ConcurrentDictionary<string, string> Values { get; } = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(Values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);

		public Task SetValue(string key, string value)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}
	}
}
