using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal static class TestDeviceLayoutConstraintTracker
{
	/// <summary>A tracker backed by the given repository (an empty one by default), for tests about
	/// something else. Never refreshed automatically - callers that need a populated snapshot call
	/// <c>RefreshAsync</c> themselves.</summary>
	public static DeviceLayoutConstraintTracker Build(IDeviceRepository? repository = null)
	{
		var services = new ServiceCollection();
		services.AddSingleton<IDeviceRepository>(repository ?? new InMemoryDeviceRepository());
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
		return new DeviceLayoutConstraintTracker(scopeFactory);
	}
}
