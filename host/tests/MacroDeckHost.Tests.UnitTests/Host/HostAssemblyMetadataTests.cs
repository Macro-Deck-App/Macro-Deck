using System.Reflection;

namespace MacroDeckHost.Tests.UnitTests.Host;

// Guards the Win32 version resource the host apphost carries (issue #79): AssemblyTitle drives the
// FileDescription that Windows Task Manager / Process Explorer show, so the spawned host process
// reads as "Macro Deck Host" instead of the raw "MacroDeckHost" assembly name.
public class HostAssemblyMetadataTests
{
	private static readonly Assembly HostAssembly = typeof(Startup).Assembly;

	[Test]
	public void Assembly_title_is_the_friendly_host_name()
	{
		var title = HostAssembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title;
		Assert.That(title, Is.EqualTo("Macro Deck Host"));
	}

	[Test]
	public void Assembly_product_is_the_friendly_host_name()
	{
		var product = HostAssembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product;
		Assert.That(product, Is.EqualTo("Macro Deck Host"));
	}

	[Test]
	public void Assembly_company_matches_the_bootstrapper_publisher()
	{
		var company = HostAssembly.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company;
		Assert.That(company, Is.EqualTo("Manuel Mayer"));
	}
}
