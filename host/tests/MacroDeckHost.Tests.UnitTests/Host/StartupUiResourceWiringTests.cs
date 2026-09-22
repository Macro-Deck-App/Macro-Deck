using System.Reflection;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Host;

[TestFixture]
internal sealed class StartupUiResourceWiringTests
{
	[Test]
	public void The_callback_router_receives_the_resource_service_and_a_throttle_of_its_own()
	{
		var services = new ServiceCollection();
		new Startup().ConfigureServices(services);
		var singletons = services
			.Where(descriptor => descriptor.Lifetime == ServiceLifetime.Singleton)
			.Select(descriptor => descriptor.ServiceType)
			.ToHashSet();

		var parameters = typeof(PluginCallbackRouter)
			.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
			.Single()
			.GetParameters();

		Assert.Multiple(() =>
		{
			Assert.That(singletons, Does.Contain(typeof(IPluginUiResources)));
			Assert.That(singletons, Does.Contain(typeof(UiResourceCallbackThrottle)));
			Assert.That(parameters.Select(parameter => parameter.ParameterType),
				Does.Contain(typeof(IPluginUiResources)).And.Contain(typeof(UiResourceCallbackThrottle)));
			Assert.That(parameters.Count(parameter => parameter.ParameterType == typeof(HostCallbackThrottle)),
				Is.EqualTo(1),
				"a second HostCallbackThrottle parameter would be handed the shared instance");
		});
	}
}
