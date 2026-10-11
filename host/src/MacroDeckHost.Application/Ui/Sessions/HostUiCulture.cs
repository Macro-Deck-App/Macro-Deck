using System.Globalization;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IHostUiCulture
{
	string Current { get; }

	string? HourCycle => null;

	void Use(string culture);
}

public sealed class HostUiCulture : IHostUiCulture
{
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly ISystemHourCycleReader _systemHourCycle;
	private volatile string? _culture;
	private volatile string? _timeFormat;

	public HostUiCulture(IServiceScopeFactory scopeFactory, ISystemHourCycleReader systemHourCycle)
	{
		_scopeFactory = scopeFactory;
		_systemHourCycle = systemHourCycle;
	}

	public string Current => _culture ??= Load(async preferences => (await preferences.GetLocalization()).Culture);

	// Resolved on every use: a time format that follows the system has to see the system switch between
	// 12 and 24 hours, which raises no notification.
	public string HourCycle
		=> TimeFormatResolver.ResolveHourCycle(_timeFormat ??= Load(preferences => preferences.GetTimeFormat()),
			CultureInfo.GetCultureInfo(Current),
			_systemHourCycle);

	public void Use(string culture)
	{
		ArgumentException.ThrowIfNullOrEmpty(culture);
		_culture = culture;
		_timeFormat = null;
	}

	private string Load(Func<IAppPreferenceService, Task<string>> read)
	{
		using var scope = _scopeFactory.CreateScope();
		var preferences = scope.ServiceProvider.GetRequiredService<IAppPreferenceService>();

		return Task.Run(() => read(preferences)).GetAwaiter().GetResult();
	}
}

public sealed class HostUiCultureChangedHandler : INotificationHandler<LocalizationCultureChangedNotification>
{
	private readonly IHostUiCulture _culture;

	public HostUiCultureChangedHandler(IHostUiCulture culture)
	{
		_culture = culture;
	}

	public ValueTask Handle(LocalizationCultureChangedNotification notification, CancellationToken cancellationToken)
	{
		_culture.Use(notification.Culture);
		return ValueTask.CompletedTask;
	}
}
