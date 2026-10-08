using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.ColorPalette;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.VideoStreams;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.ColorPalette;

[TestFixture]
public class ColorPaletteServiceTests
{
	private MemoryPreferences _preferences = null!;
	private RecordingPublisher _publisher = null!;
	private ServiceProvider _services = null!;

	[SetUp]
	public void SetUp()
	{
		_preferences = new MemoryPreferences();
		_publisher = new RecordingPublisher();
		_services = new ServiceCollection()
			.AddScoped<IAppPreferenceRepository>(_ => _preferences)
			.BuildServiceProvider();
	}

	[TearDown]
	public void TearDown() => _services.Dispose();

	private ColorPaletteService CreateService()
		=> new(_services.GetRequiredService<IServiceScopeFactory>(), _publisher);

	[Test]
	public async Task A_fresh_palette_offers_the_default_colours()
	{
		using var palette = CreateService();

		Assert.That(await palette.GetColors(), Is.EqualTo(ColorPaletteService.DefaultColors));
	}

	[Test]
	public async Task Added_colours_follow_the_defaults_and_are_remembered_across_a_host_restart()
	{
		using (var palette = CreateService())
		{
			await palette.SetColor("#3ff4ee", true);
			await palette.SetColor("#ef444480", true);
		}

		using var restarted = CreateService();

		Assert.That(await restarted.GetColors(),
			Is.EqualTo(ColorPaletteService.DefaultColors.Concat(["#3ff4ee", "#ef444480"])));
	}

	[Test]
	public async Task A_removed_default_colour_stays_removed_and_an_emptied_palette_stays_empty()
	{
		using (var palette = CreateService())
		{
			await palette.SetColor("#ef4444", false);
		}

		using (var restarted = CreateService())
		{
			Assert.That(await restarted.GetColors(), Does.Not.Contain("#ef4444"));
			foreach (var color in await restarted.GetColors())
			{
				await restarted.SetColor(color, false);
			}
		}

		using var emptied = CreateService();

		Assert.That(await emptied.GetColors(), Is.Empty);
	}

	[Test]
	public async Task Restoring_the_defaults_replaces_every_change_and_tells_every_admin_ui()
	{
		using var palette = CreateService();
		await palette.SetColor("#ef4444", false);
		await palette.SetColor("#3ff4ee", true);

		var restored = await palette.RestoreDefaults();

		Assert.Multiple(async () =>
		{
			Assert.That(restored, Is.EqualTo(ColorPaletteService.DefaultColors));
			Assert.That(await palette.GetColors(), Is.EqualTo(ColorPaletteService.DefaultColors));
			Assert.That(_publisher.Of<ColorPaletteChangedNotification>()[^1].Colors,
				Is.EqualTo(ColorPaletteService.DefaultColors));
		});
	}

	[TestCase("#3FF4EE", "#3ff4ee")]
	[TestCase("#abc", "#aabbcc")]
	[TestCase("#3ff4eeff", "#3ff4ee")]
	[TestCase(" #12345678 ", "#12345678")]
	public async Task A_colour_is_stored_in_the_form_the_picker_writes(string input, string stored)
	{
		using var palette = CreateService();

		var result = await palette.SetColor(input, true);

		Assert.That(result.Colors[^1], Is.EqualTo(stored));
	}

	[Test]
	public async Task The_same_colour_in_another_spelling_is_not_added_twice()
	{
		using var palette = CreateService();
		await palette.SetColor("#aabbcc", true);

		var result = await palette.SetColor("#ABC", true);

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(ColorPaletteOutcome.Unchanged));
			Assert.That(result.Colors.Count(color => color == "#aabbcc"), Is.EqualTo(1));
			Assert.That(_publisher.Of<ColorPaletteChangedNotification>(), Has.Count.EqualTo(1));
		});
	}

	[TestCase("")]
	[TestCase("transparent")]
	[TestCase("{{ vars.primary | color }}")]
	[TestCase("#12345")]
	[TestCase("#gggggg")]
	public async Task Something_that_is_not_a_fixed_colour_is_refused(string input)
	{
		var handler = new SetColorPaletteEntryRequestMessageHandler(CreateService());

		var response = await handler.Handle(new SetColorPaletteEntryRequest { Color = input, Present = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(SetColorPaletteEntryRequestMessageHandler.InvalidColorCode));
			Assert.That(response.Colors, Is.EqualTo(ColorPaletteService.DefaultColors));
		});
	}

	[Test]
	public async Task Removing_a_colour_drops_only_that_colour_and_tells_every_admin_ui()
	{
		using var palette = CreateService();
		await palette.SetColor("#111111", true);

		var result = await palette.SetColor("#111111", false);

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(ColorPaletteOutcome.Updated));
			Assert.That(result.Colors, Is.EqualTo(ColorPaletteService.DefaultColors));
			Assert.That(_publisher.Of<ColorPaletteChangedNotification>().Select(n => n.Colors.Contains("#111111")),
				Is.EqualTo(new[] { true, false }));
		});
	}

	[Test]
	public async Task Two_clients_adding_at_once_both_keep_their_colour()
	{
		using var palette = CreateService();

		await Task.WhenAll(palette.SetColor("#111111", true), palette.SetColor("#222222", true));

		Assert.That(await palette.GetColors(), Is.SupersetOf(new[] { "#111111", "#222222" }));
	}

	[Test]
	public async Task Adding_to_a_full_palette_is_refused_instead_of_dropping_an_older_colour()
	{
		var full = Enumerable.Range(0, ColorPaletteService.MaxColors).Select(i => $"#0000{i:x2}");
		_preferences.Values[AppPreferenceService.ColorPaletteKey] = JsonSerializer.Serialize(full);
		var handler = new SetColorPaletteEntryRequestMessageHandler(CreateService());

		var response = await handler.Handle(new SetColorPaletteEntryRequest { Color = "#ffffff", Present = true },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(SetColorPaletteEntryRequestMessageHandler.LimitReachedCode));
			Assert.That(response.Colors, Has.Count.EqualTo(ColorPaletteService.MaxColors));
			Assert.That(response.Colors, Does.Not.Contain("#ffffff"));
		});
	}

	[Test]
	public async Task An_unreadable_stored_value_reads_as_the_default_palette()
	{
		_preferences.Values[AppPreferenceService.ColorPaletteKey] = "{not json";
		var handler = new GetColorPaletteRequestMessageHandler(CreateService());

		var response = await handler.Handle(new GetColorPaletteRequest(), CancellationToken.None);

		Assert.That(response.Colors, Is.EqualTo(ColorPaletteService.DefaultColors));
	}

	[Test]
	public async Task The_change_reaches_admin_uis_only_never_the_deck_clients()
	{
		var transport = new Auth.RecordingUiTransport();
		var handler = new ColorPaletteChangedNotificationHandler(transport);

		await handler.Handle(new ColorPaletteChangedNotification(["#3ff4ee"]), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(transport.Sent, Is.Empty);
			var (group, message) = transport.GroupMessages.Single();
			Assert.That(group, Is.EqualTo(UiAdminGroups.Admin));
			Assert.That(((ColorPaletteChangedEvent)message).Colors, Is.EqualTo(new[] { "#3ff4ee" }));
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
