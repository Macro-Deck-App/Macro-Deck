using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.VideoStreams;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class WidgetTypeFavoritesServiceTests
{
	private const string PluginType = "com.example.gauges::gauge";

	private MemoryPreferences _preferences = null!;
	private RecordingPublisher _publisher = null!;
	private WidgetTypeRegistry _registry = null!;
	private ServiceProvider _services = null!;

	[SetUp]
	public async Task SetUp()
	{
		_preferences = new MemoryPreferences();
		_publisher = new RecordingPublisher();
		_registry = new WidgetTypeRegistry(new RecordingPublisher());
		await _registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge", LocalizedText.FromLiteral("Gauge")));
		_services = new ServiceCollection()
			.AddScoped<IAppPreferenceRepository>(_ => _preferences)
			.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown() => _services.Dispose();

	private WidgetTypeFavoritesService CreateService()
		=> new(_services.GetRequiredService<IServiceScopeFactory>(), _registry, _publisher);

	[Test]
	public async Task A_starred_type_is_remembered_across_a_host_restart()
	{
		using (var favorites = CreateService())
		{
			await favorites.SetFavorite(WidgetTypeIds.Clock, true);
			await favorites.SetFavorite(PluginType, true);
		}

		using var restarted = CreateService();

		Assert.That(await restarted.GetFavorites(), Is.EqualTo(new[] { WidgetTypeIds.Clock, PluginType }));
	}

	[Test]
	public async Task Unstarring_removes_the_type_and_tells_every_admin_ui()
	{
		using var favorites = CreateService();
		await favorites.SetFavorite(WidgetTypeIds.Clock, true);

		var result = await favorites.SetFavorite(WidgetTypeIds.Clock, false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(WidgetTypeFavoriteOutcome.Updated));
			Assert.That(result.TypeIds, Is.Empty);
			Assert.That(_publisher.Of<WidgetTypeFavoritesChangedNotification>().Select(n => n.TypeIds.Count),
				Is.EqualTo(new[] { 1, 0 }));
		});
	}

	[Test]
	public async Task A_type_nobody_provides_cannot_be_starred()
	{
		using var favorites = CreateService();

		var result = await favorites.SetFavorite("com.example.missing::thing", true);
		var stored = await favorites.GetFavorites();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(WidgetTypeFavoriteOutcome.UnknownType));
			Assert.That(stored, Is.Empty);
			Assert.That(_publisher.Published, Is.Empty);
		});
	}

	[Test]
	public async Task A_favorite_whose_plugin_is_gone_stays_starred_and_can_still_be_unstarred()
	{
		using var favorites = CreateService();
		await favorites.SetFavorite(PluginType, true);
		await _registry.UnregisterAll("com.example.gauges");

		Assert.That(await favorites.GetFavorites(), Does.Contain(PluginType));

		var result = await favorites.SetFavorite(PluginType, false);
		var remaining = await favorites.GetFavorites();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(WidgetTypeFavoriteOutcome.Updated));
			Assert.That(remaining, Is.Empty);
		});
	}

	[Test]
	public async Task Repeating_the_current_state_changes_nothing_and_broadcasts_nothing()
	{
		using var favorites = CreateService();
		await favorites.SetFavorite(WidgetTypeIds.Clock, true);

		var again = await favorites.SetFavorite(WidgetTypeIds.Clock, true);
		var notStarred = await favorites.SetFavorite(WidgetTypeIds.Weather, false);

		Assert.Multiple(() =>
		{
			Assert.That(again.Outcome, Is.EqualTo(WidgetTypeFavoriteOutcome.Unchanged));
			Assert.That(notStarred.Outcome, Is.EqualTo(WidgetTypeFavoriteOutcome.Unchanged));
			Assert.That(_publisher.Of<WidgetTypeFavoritesChangedNotification>(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Starring_beyond_the_limit_is_refused_instead_of_dropping_an_older_favorite()
	{
		var full = Enumerable.Range(0, WidgetTypeFavoritesService.MaxFavorites).Select(i => $"com.example.p{i}::t");
		_preferences.Values[AppPreferenceService.WidgetTypeFavoritesKey] =
			JsonSerializer.Serialize(full);
		var handler = new SetWidgetTypeFavoriteRequestMessageHandler(CreateService());

		var response = await handler.Handle(
			new SetWidgetTypeFavoriteRequest { WidgetTypeId = WidgetTypeIds.Clock, Favorite = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(SetWidgetTypeFavoriteRequestMessageHandler.LimitReachedCode));
			Assert.That(response.TypeIds, Has.Count.EqualTo(WidgetTypeFavoritesService.MaxFavorites));
			Assert.That(response.TypeIds, Does.Not.Contain(WidgetTypeIds.Clock));
		});
	}

	[Test]
	public async Task An_unreadable_stored_value_reads_as_no_favorites()
	{
		_preferences.Values[AppPreferenceService.WidgetTypeFavoritesKey] = "{not json";
		using var favorites = CreateService();

		Assert.That(await favorites.GetFavorites(), Is.Empty);
	}

	[Test]
	public async Task The_change_reaches_admin_uis_only_never_the_deck_clients()
	{
		var transport = new Auth.RecordingUiTransport();
		var handler = new WidgetTypeFavoritesChangedNotificationHandler(transport);

		await handler.Handle(new WidgetTypeFavoritesChangedNotification([WidgetTypeIds.Clock]), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(transport.Sent, Is.Empty);
			var (group, message) = transport.GroupMessages.Single();
			Assert.That(group, Is.EqualTo(UiAdminGroups.Admin));
			Assert.That(((WidgetTypeFavoritesChangedEvent)message).TypeIds, Is.EqualTo(new[] { WidgetTypeIds.Clock }));
		});
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
