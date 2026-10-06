using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Timers;
using MacroDeckHost.Widgets.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

internal sealed class CalendarWidgetHarness
{
	public const string Client = "client-1";

	public CalendarWidgetHarness()
	{
		Google = new FakeCalendarIntegration("app.google", "Google Calendar").WithAccount("alice", "main", "team");
		Cache = CalendarTesting.Cache(Time, TimeZoneInfo.Utc, Google);
		Provider = new CalendarUiProvider(Cache,
			Folders,
			WidgetChanges,
			Opener,
			Time,
			ScopeFactory(Preferences),
			TestLocalization.SampleText,
			Logger.None);
	}

	public FakeTimeProvider Time { get; } = new() { Now = CalendarTesting.Noon };

	public FakeLocalizationPreferences Preferences { get; } = new();

	public FakeCalendarIntegration Google { get; }

	public CalendarEventCache Cache { get; }

	public RecordingUrlOpener Opener { get; } = new();

	public RecordingUiInteractions Interactions { get; } = new();

	public MutableFolderCache Folders { get; } = new();

	public CalendarWidgetChanges WidgetChanges { get; } = new();

	public CalendarUiProvider Provider { get; }

	public static string CalendarKey(string calendarId = "main", string account = "alice")
		=> CalendarKeys.Calendar("app.google::" + account, calendarId);

	public CalendarWidgetHarness With(CalendarEvent calendarEvent, string account = "alice")
	{
		Google.WithEvent(account, calendarEvent);
		return this;
	}

	public Task SyncAsync() => Cache.SyncAsync(CancellationToken.None);

	public async Task<IUiSession> OpenWidgetAsync(string layout, object data, bool sample = false)
		=> (await Provider.CreateSessionAsync(
			new UiSessionRequest
			{
				Surface = WidgetSurface(CalendarWidgetTypes.QualifiedId, WithLayout(layout, data), sample),
				UiModelVersion = 1,
			},
			CancellationToken.None))!;

	public async Task<IUiSession> OpenConfigAsync(object data)
		=> (await Provider.CreateSessionAsync(
			new UiSessionRequest { Surface = ConfigSurface(CalendarWidgetTypes.QualifiedId, data), UiModelVersion = 1 },
			CancellationToken.None))!;

	public static JsonElement WithLayout(string layout, object data)
	{
		var node = JsonSerializer.SerializeToNode(data) as JsonObject ?? [];
		node[CalendarWidgetTypes.LayoutKey] = layout;

		return JsonSerializer.SerializeToElement(node);
	}

	public static string WithLayout(string layout, string data)
		=> WithLayout(layout, (object)JsonDocument.Parse(data).RootElement).GetRawText();

	public async Task<IUiSession> OpenDialogAsync(string calendarKey, string eventId)
		=> (await Provider.CreateSessionAsync(
			new UiSessionRequest { Surface = DialogSurface(calendarKey, eventId), UiModelVersion = 1 },
			CancellationToken.None))!;

	public async Task<IUiSession> OpenAgendaDialogAsync(Guid widgetId)
		=> (await Provider.CreateSessionAsync(
			new UiSessionRequest { Surface = AgendaDialogSurface(widgetId), UiModelVersion = 1 },
			CancellationToken.None))!;

	public static UiSurface WidgetSurface(string widgetType, object data, bool sample = false)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
			[UiWidgetSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(data),
		};

		if (sample)
		{
			attributes[UiWidgetSurfaceAttributes.Sample] = JsonSerializer.SerializeToElement(true);
		}
		else
		{
			attributes[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString());
		}

		return new UiSurface
		{
			Kind = sample ? UiSurfaceKinds.Preview : UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = attributes,
		};
	}

	public static UiSurface DialogSurface(string calendarKey, string eventId)
		=> new()
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiDialogSurfaceAttributes.ModalId] = JsonSerializer.SerializeToElement("modal-1"),
				[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(CalendarUiProvider.DialogViewId),
				[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{
					[CalendarUiProvider.DialogCalendarKey] = calendarKey,
					[CalendarUiProvider.DialogEventKey] = eventId,
				}),
			},
		};

	public static UiSurface AgendaDialogSurface(Guid widgetId)
		=> new()
		{
			Kind = UiSurfaceKinds.Dialog,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiDialogSurfaceAttributes.ModalId] = JsonSerializer.SerializeToElement("modal-1"),
				[UiDialogSurfaceAttributes.ViewId] = JsonSerializer.SerializeToElement(CalendarUiProvider.AgendaDialogViewId),
				[UiDialogSurfaceAttributes.Data] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
				{
					[CalendarUiProvider.AgendaDialogWidgetKey] = widgetId.ToString(),
				}),
			},
		};

	public static UiSurface ConfigSurface(string widgetType, object data)
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] = JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.SerializeToElement(data),
			},
		};

	public static IServiceScopeFactory ScopeFactory(FakeLocalizationPreferences? preferences = null)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceService>(preferences ?? new FakeLocalizationPreferences());
		services.AddSingleton<ISystemHourCycleReader>(new TwentyFourHourClock());
		services.AddScoped<TimeFormatResolver>();
		return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
	}

	private sealed class TwentyFourHourClock : ISystemHourCycleReader
	{
		public string? Read() => HourCycles.H23;
	}
}

internal static class CalendarFolders
{
	public static WidgetEntity AddCalendar(this MutableFolderCache folders, string layout, string data)
		=> folders.Add(CalendarWidgetTypes.QualifiedId, CalendarWidgetHarness.WithLayout(layout, data));
}

internal static class CalendarTree
{
	public static IEnumerable<UiNode> Flatten(UiNode node)
	{
		yield return node;

		foreach (var child in node.Children.SelectMany(Flatten))
		{
			yield return child;
		}
	}

	public static UiNode? Find(UiNode root, string key)
		=> Flatten(root).FirstOrDefault(node => node.Id == key || node.Id.EndsWith("." + key, StringComparison.Ordinal));

	public static UiNode Get(UiNode root, string key)
		=> Find(root, key) ?? throw new AssertionException($"No node keyed '{key}' is in the tree.");

	public static string? Text(UiNode node, string culture = "en")
		=> node.Properties.TryGetValue(UiComponentProperties.Text, out var text)
			? TestLocalization.Resolve(Read(text), culture)
			: null;

	// The converter reads arguments back as plain values only, so a reference nested in another one is
	// rebuilt here the way the clients read it.
	public static LocalizedText Read(JsonElement value)
	{
		if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("$localized", out var reference))
		{
			return value.Deserialize<LocalizedText>();
		}

		var arguments = new Dictionary<string, object?>(StringComparer.Ordinal);

		if (reference.TryGetProperty("arguments", out var declared))
		{
			foreach (var argument in declared.EnumerateObject())
			{
				arguments[argument.Name] = argument.Value.ValueKind switch
				{
					JsonValueKind.Object => Read(argument.Value),
					JsonValueKind.String => argument.Value.GetString(),
					JsonValueKind.Number when argument.Value.TryGetInt32(out var count) => count,
					JsonValueKind.Number => argument.Value.GetDouble(),
					JsonValueKind.True => true,
					JsonValueKind.False => false,
					_ => null,
				};
			}
		}

		return LocalizedText.FromLocalized(new LocalizedString(
			new LocalizationKey(reference.GetProperty("scope").GetString()!, reference.GetProperty("key").GetString()!),
			arguments));
	}

	public static List<string> Texts(UiNode root, string culture = "en")
		=> [.. Flatten(root).Select(node => Text(node, culture)).OfType<string>()];

	public static string Resolve(LocalizedText text, string culture = "en") => TestLocalization.Resolve(text, culture)!;

	public static bool IsPressable(UiNode node)
		=> node.Properties.TryGetValue(UiComponentProperties.Events, out var events) &&
			events.EnumerateArray().Any(item => item.GetRawText().Contains(UiComponentEvents.Press, StringComparison.Ordinal));

	public static void Press(IUiSession session, UiNode node)
		=> session.Dispatch(new UiEvent { NodeId = node.Id, Name = UiComponentEvents.Press });

	public static async Task WaitForAsync(Func<bool> condition, string failure)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);

		while (!condition())
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), failure);
			await Task.Delay(10);
		}
	}
}
