using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Calendar;

public interface ICalendarHostServicesConsumer
{
	void UseCalendarServices(CalendarHostServices services);
}

[MacroDeckIntegration]
public sealed class CalendarIntegration : IIntegration, ISystemIntegration, IWidgetTypeProvider,
	ICalendarHostServicesConsumer
{
	public const string IntegrationId = CalendarWidgetTypes.OwnerId;

	private const string DataSchema
		= """{"type":"object","properties":{"layout":{"type":"string","enum":["agenda","next-event"]},"calendars":{"type":"array","items":{"type":"string"}},"days":{"type":"integer","minimum":1,"maximum":7},"showDate":{"type":"boolean"},"showAllDay":{"type":"boolean"},"showTime":{"type":"boolean"},"showLocation":{"type":"boolean"},"showCalendar":{"type":"boolean"},"whenStarted":{"type":"string","enum":["now","next"]},"leadTime":{"type":"integer","minimum":1,"maximum":60},"flows":{"type":["string","array"]},"backgroundColor":{"type":["string","null"],"description":"#rrggbb or transparent"}}}""";

	private CalendarHostServices? _services;

	public CalendarIntegration()
	{
		Actions =
		[
			new JoinMeetingActionDefinition(() => _services),
			new ShowDetailsActionDefinition(() => _services),
		];
	}

	public string Id => IntegrationId;

	public LocalizedText Name => AppStrings.Integrations.Calendar.Name();

	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;

	public bool IsInitialized => true;

	public void UseCalendarServices(CalendarHostServices services) => _services = services;

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public async Task InitializeAsync(IWidgetTypeProviderContext context, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(context);

		foreach (var widgetType in GetWidgetTypes())
		{
			await context.RegisterWidgetTypeAsync(widgetType, cancellationToken);
		}
	}

	public IReadOnlyList<WidgetTypeDescriptor> GetWidgetTypes() => [CalendarWidgetType()];

	// showAllDay is left out on purpose: each layout has its own default until the widget is first saved.
	private static WidgetTypeDescriptor CalendarWidgetType()
		=> new(CalendarWidgetTypes.LocalId,
			AppStrings.Integrations.Calendar.Name(),
			AppStrings.Integrations.Calendar.Widget.Description(),
			DefaultData: """{"layout":"agenda","calendars":[],"days":1,"showDate":true,"showTime":true,"showLocation":false,"showCalendar":false,"whenStarted":"now"}""",
			DataSchema: DataSchema,
			HasConfiguration: true)
		{
			AppearanceProperties = [WidgetAppearanceProperty.BackgroundColor],
			SupportsFlows = true,
		};
}
