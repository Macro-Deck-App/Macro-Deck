using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Host;

[TestFixture]
internal sealed class StartupServiceLifetimeTests
{
	/// <summary>
	/// Every other test builds its collaborators directly, so a captive dependency - one of our own
	/// singletons taking a scoped service - passes all of them and then fails at boot with nothing but a
	/// stack trace. That is exactly how a singleton VariableBroadcaster taking the scoped
	/// IVariableBindingService shipped green once already.
	///
	/// The registrations are inspected rather than the provider built: ConfigureServices alone is not a
	/// runnable container (it wants IHostEnvironment and the rest of the ASP.NET Core web host), and
	/// standing one up would test the framework's composition instead of our lifetimes.
	/// </summary>
	[Test]
	public void No_singleton_of_ours_captures_a_scoped_service()
	{
		var services = new ServiceCollection();
		new Startup().ConfigureServices(services);

		Assert.That(CaptiveDependencies(services), Is.Empty);
	}

	/// <summary>
	/// Proves the scan above can actually fail. Reintroducing the real defect is not a usable check here
	/// - the production constructor no longer accepts the scoped service, so putting it back stops the
	/// test project compiling rather than turning this red - so the detection is exercised against a
	/// collection built to contain exactly the shape it hunts for.
	/// </summary>
	[Test]
	public void The_scan_reports_a_singleton_that_captures_a_scoped_service()
	{
		var services = new ServiceCollection();
		services.AddScoped<ICapturedScopedService, CapturedScopedService>();
		services.AddSingleton<CapturingSingleton>();

		Assert.That(CaptiveDependencies(services),
			Has.Exactly(1).Contains(nameof(CapturingSingleton)).And.One.Contains(nameof(ICapturedScopedService)));
	}

	private static List<string> CaptiveDependencies(IServiceCollection services)
	{
		var scopedServiceTypes = services
			.Where(descriptor => descriptor.Lifetime == ServiceLifetime.Scoped)
			.Select(descriptor => descriptor.ServiceType)
			.ToHashSet();

		var captives = new List<string>();

		foreach (var descriptor in services)
		{
			if (descriptor.Lifetime != ServiceLifetime.Singleton ||
				descriptor.ImplementationType is not { } implementation ||
				!IsOurs(implementation))
			{
				continue;
			}

			foreach (var parameter in ConstructorParameters(implementation))
			{
				if (scopedServiceTypes.Contains(parameter))
				{
					captives.Add($"{implementation.Name} (singleton) takes {parameter.Name} (scoped)");
				}
			}
		}

		return captives;
	}

	private interface ICapturedScopedService;

	private sealed class CapturedScopedService : ICapturedScopedService;

	private sealed class CapturingSingleton
	{
		public CapturingSingleton(ICapturedScopedService captured)
		{
			_ = captured;
		}
	}

	private static bool IsOurs(Type type)
		=> type.Namespace?.StartsWith("MacroDeckHost", StringComparison.Ordinal) == true;

	private static IEnumerable<Type> ConstructorParameters(Type implementation)
		=> implementation
			.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
			.SelectMany(constructor => constructor.GetParameters())
			.Select(parameter => parameter.ParameterType);
}
