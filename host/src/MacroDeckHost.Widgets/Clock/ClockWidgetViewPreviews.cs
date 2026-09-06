using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeckHost.Widgets.Clock;

internal static class ClockWidgetViewPreviews
{
	[UiPreview("Digital", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Digital()
		=> ClockWidgetView.Build(new ClockWidgetData { ShowSeconds = true, ShowDate = true });

	[UiPreview("Analog", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Analog()
		=> ClockWidgetView.Build(new ClockWidgetData { IsAnalog = true, ShowSeconds = true, ShowDate = true });

	[UiPreview("Labeled", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Labeled()
		=> ClockWidgetView.Build(new ClockWidgetData
		{
			ShowLabel = true, Label = "Home Office", ShowSeconds = true, ShowDate = true,
		});

	[UiPreview("Minimal", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Minimal()
		=> ClockWidgetView.Build(new ClockWidgetData { ShowSeconds = false, ShowDate = false });

	[UiPreview("Tinted", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Tinted()
		=> ClockWidgetView.Build(new ClockWidgetData
		{
			BackgroundColor = "#101c2c",
			TextColor = "#7fd4ff",
			HourCycle = ClockHourCycle.TwelveHour,
			LeadingZero = false,
			ShowSeconds = false,
			ShowDate = true,
			DateFormat = ClockDateFormat.Iso,
		});

	[UiPreview("Date beside the time", Profile = UiPreviewProfiles.Widget)]
	public static UiElement DateBeside()
		=> ClockWidgetView.Build(new ClockWidgetData
		{
			HourCycle = ClockHourCycle.TwentyFourHour,
			LeadingZero = false,
			ShowSeconds = false,
			ShowDate = true,
			DateFormat = ClockDateFormat.DayFirst,
			DatePosition = ClockDatePosition.Right,
		});

	[UiPreview("Zoned", Profile = UiPreviewProfiles.Widget)]
	public static UiElement Zoned()
		=> ClockWidgetView.Build(new ClockWidgetData
		{
			TimeZone = "America/New_York",
			ShowLabel = true,
			ShowSeconds = false,
			ShowDate = true,
			ShowOffset = true,
			DateFormat = ClockDateFormat.Written,
		});
}
