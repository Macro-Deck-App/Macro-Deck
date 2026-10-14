using System.Globalization;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Locale;
using MacroDeckHost.Application.FolderViews;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Modals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
internal sealed class LocaleCallbackRoutingTests
{
	[Test]
	public async Task A_plugin_reads_the_culture_and_hour_cycle_the_host_resolved()
	{
		var router = Router(new FixedLocale(new TimeOfDayFormat(CultureInfo.GetCultureInfo("de-DE"), HourCycles.H12)));

		var result = await InvokeAsync(router);

		var answer = result.Data!.Value.Deserialize<LocaleTimeOfDayFormatResult>(PluginProtocolJson.Options)!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(answer.Culture, Is.EqualTo("de-DE"));
			Assert.That(answer.HourCycle, Is.EqualTo(HourCycles.H12));
		});
	}

	[Test]
	public async Task The_answer_follows_the_users_time_format_setting()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IAppPreferenceService>(new FakeLocalizationPreferences
		{
			Culture = "en-US",
			TimeFormat = AppPreferenceService.TimeFormat24H
		});
		services.AddSingleton<ISystemHourCycleReader>(new SystemTimePatternHourCycleReader(() => "h:mm tt"));
		services.AddScoped<TimeFormatResolver>();
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var result = await InvokeAsync(Router(new HostLocaleApi(scopeFactory)));

		var answer = result.Data!.Value.Deserialize<LocaleTimeOfDayFormatResult>(PluginProtocolJson.Options)!;
		Assert.Multiple(() =>
		{
			Assert.That(answer.Culture, Is.EqualTo("en-US"));
			Assert.That(answer.HourCycle, Is.EqualTo(HourCycles.H23));
		});
	}

	[Test]
	public async Task A_host_without_a_locale_source_answers_unsupported()
	{
		var result = await InvokeAsync(Router(null));

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	private static Task<HostCallbackResult> InvokeAsync(PluginCallbackRouter router)
	{
		return router.RouteAsync("com.example.locale",
			"session-1",
			Guid.CreateVersion7().ToString(),
			new HostInvokePayload { Api = HostApis.Locale, Operation = HostOperations.Locale.TimeOfDayFormat },
			CancellationToken.None);
	}

	private static PluginCallbackRouter Router(ILocaleApi? locale)
	{
		return new PluginCallbackRouter(new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			null!,
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			null!,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			null!,
			new NoOpUiSessionSink(),
			new FakePluginDeviceRegistry(),
			new LayoutRegistry(new RecordingMediator()),
			new FolderViewRegistry(new RecordingMediator()),
			new WidgetTypeRegistry(new RecordingMediator()),
			new ScreenSaverRegistry(new RecordingMediator()),
			new ModalInteractionCoordinator(TimeProvider.System),
			null!,
			new HostCallbackThrottle(TimeProvider.System, 1000, 1000),
			new FakeHostLockState(),
			Serilog.Core.Logger.None,
			locale: locale);
	}

	private sealed class FixedLocale(TimeOfDayFormat format) : ILocaleApi
	{
		public Task<TimeOfDayFormat> GetTimeOfDayFormatAsync(CancellationToken cancellationToken = default)
		{
			return Task.FromResult(format);
		}
	}
}
