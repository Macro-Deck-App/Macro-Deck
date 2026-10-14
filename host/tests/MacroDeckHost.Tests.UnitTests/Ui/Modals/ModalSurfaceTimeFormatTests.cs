using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Sdk.Locale;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.Modals;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Ui.Modals;

[TestFixture]
public class ModalSurfaceTimeFormatTests
{
	[Test]
	public void A_dialog_learns_the_language_and_hour_cycle_its_reader_uses()
	{
		var coordinator = new ModalInteractionCoordinator(new ManualTimeProvider());
		var broker = new RecordingUiSessionBroker();
		var modalId
			= coordinator.Register("com.example.plugin", "client-a", new ModalDefinition { ViewId = "details" })!;
		var opener = new ModalUiSessionOpener(coordinator,
			broker,
			Serilog.Core.Logger.None,
			new FixedCulture("de", "h12"));

		opener.Open(new OpenModalUiSessionRequest { ModalId = modalId }, "device-a");

		var attributes = broker.LastOpenedSurface!.Attributes;
		Assert.Multiple(() =>
		{
			Assert.That(attributes[UiDialogSurfaceAttributes.Culture].GetString(), Is.EqualTo("de"));
			Assert.That(attributes[UiDialogSurfaceAttributes.HourCycle].GetString(), Is.EqualTo("h12"));
		});
	}

	[Test]
	public void A_time_format_that_follows_the_system_sees_the_system_switch_on_the_next_dialog()
	{
		var coordinator = new ModalInteractionCoordinator(new ManualTimeProvider());
		var broker = new RecordingUiSessionBroker();
		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceService>(new FakeLocalizationPreferences
		{
			Culture = "en-US",
			TimeFormat = AppPreferenceService.TimeFormatSystem
		});
		var system = new SwitchableHourCycle { Value = HourCycles.H12 };
		var culture = new HostUiCulture(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			system);
		var opener = new ModalUiSessionOpener(coordinator, broker, Serilog.Core.Logger.None, culture);

		var first = OpenHourCycle(coordinator, broker, opener);
		system.Value = HourCycles.H23;
		var second = OpenHourCycle(coordinator, broker, opener);

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.EqualTo(HourCycles.H12));
			Assert.That(second, Is.EqualTo(HourCycles.H23));
		});
	}

	private static string? OpenHourCycle(
		ModalInteractionCoordinator coordinator,
		RecordingUiSessionBroker broker,
		ModalUiSessionOpener opener)
	{
		var modalId
			= coordinator.Register("com.example.plugin", "client-a", new ModalDefinition { ViewId = "details" })!;
		opener.Open(new OpenModalUiSessionRequest { ModalId = modalId }, "device-a");

		return broker.LastOpenedSurface!.Attributes[UiDialogSurfaceAttributes.HourCycle].GetString();
	}

	private sealed class SwitchableHourCycle : ISystemHourCycleReader
	{
		public string? Value { get; set; }

		public string? Read()
		{
			return Value;
		}
	}

	private sealed class FixedCulture(string culture, string hourCycle) : IHostUiCulture
	{
		public string Current => culture;

		public string HourCycle => hourCycle;

		public void Use(string value)
		{
		}
	}
}
