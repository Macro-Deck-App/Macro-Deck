using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Widgets;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestTimerCoordinators
{
	public static TimerWidgetCoordinator Unused(IFolderCache folders, IWidgetTriggerService triggers)
	{
		var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		var store = new TimerWidgetStore(TimeProvider.System);

		return new TimerWidgetCoordinator(store,
			new TimerWidgetVariableWriter(store, scopes, Serilog.Core.Logger.None),
			triggers,
			new NoPrompt(),
			folders,
			new FakeHostLockState(),
			scopes,
			Serilog.Core.Logger.None);
	}

	private sealed class NoPrompt : ICountdownDurationPrompt
	{
		public Task<int?> AskAsync(Guid widgetId, string originClientId, int? initialSeconds,
			CancellationToken cancellationToken)
			=> Task.FromResult<int?>(null);
	}
}
