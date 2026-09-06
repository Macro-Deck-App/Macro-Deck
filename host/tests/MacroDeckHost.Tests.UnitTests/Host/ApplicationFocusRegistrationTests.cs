using MacroDeckHost.Application.Deck;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Deck;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Host;

[TestFixture]
internal sealed class ApplicationFocusRegistrationTests
{
	[Test]
	public void IApplicationFocusWatcher_IsRegisteredAsASingleton()
	{
		var services = new ServiceCollection();
		new Startup().ConfigureServices(services);

		var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IApplicationFocusWatcher));

		Assert.That(descriptor, Is.Not.Null);
		Assert.Multiple(() =>
		{
			// Singleton-ness is what guarantees one native subscription per process; a factory
			// registration returning a fresh instance per call would satisfy the lifetime check above
			// while still breaking that guarantee, so the implementation type must be pinned down too.
			Assert.That(descriptor!.Lifetime, Is.EqualTo(ServiceLifetime.Singleton));
			Assert.That(descriptor!.ImplementationType, Is.EqualTo(typeof(FocusedApplicationWatcher)));
		});
	}

	[Test]
	public void ApplicationFocusBackgroundService_HasExactlyOneHostedServiceRegistration()
	{
		var services = new ServiceCollection();
		new Startup().ConfigureServices(services);

		var hostedServiceDescriptors = services.Where(d =>
			d.ServiceType == typeof(IHostedService) &&
			d.ImplementationType == typeof(ApplicationFocusBackgroundService));

		Assert.That(hostedServiceDescriptors, Has.Exactly(1).Items);
	}
}
