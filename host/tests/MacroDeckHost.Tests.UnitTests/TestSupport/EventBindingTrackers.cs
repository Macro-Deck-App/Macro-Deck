using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class EventBindingTrackers
{
	public static EventBindingTracker Create(IEventSubscriptionIndex index)
	{
		return new EventBindingTracker(index,
			new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
			new VariableChangeNotifier(),
			Serilog.Core.Logger.None);
	}
}
