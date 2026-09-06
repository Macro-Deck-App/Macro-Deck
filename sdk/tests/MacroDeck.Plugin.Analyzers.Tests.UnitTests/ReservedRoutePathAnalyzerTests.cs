using MacroDeck.Plugin.Analyzers.Rules;
using MacroDeck.Plugin.Analyzers.Tests.UnitTests.Support;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

[TestFixture]
public class ReservedRoutePathAnalyzerTests
{
	[Test]
	public async Task Fires_when_a_mapped_path_falls_under_the_reserved_prefix()
	{
		const string source = """
							  using Microsoft.AspNetCore.Builder;
							  using Microsoft.AspNetCore.Routing;

							  internal static class Endpoints
							  {
							  	public static void Map(IEndpointRouteBuilder endpoints)
							  	{
							  		endpoints.MapGet("/_macrodeck/custom", () => "hi");
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new ReservedRoutePathAnalyzer());

		Assert.That(diagnostics, Has.One.Items);
		Assert.That(diagnostics[0].Id, Is.EqualTo("MDP2005"));
		Assert.That(AnalyzerTestHarness.GetSquiggleText(diagnostics[0], source), Is.EqualTo("\"/_macrodeck/custom\""));
	}

	[Test]
	public async Task Does_not_fire_when_a_mapped_path_falls_outside_the_reserved_prefix()
	{
		const string source = """
							  using Microsoft.AspNetCore.Builder;
							  using Microsoft.AspNetCore.Routing;

							  internal static class Endpoints
							  {
							  	public static void Map(IEndpointRouteBuilder endpoints)
							  	{
							  		endpoints.MapGet("/my-plugin/status", () => "hi");
							  	}
							  }
							  """;

		var diagnostics = await AnalyzerTestHarness.GetDiagnosticsAsync(source, new ReservedRoutePathAnalyzer());

		Assert.That(diagnostics, Is.Empty);
	}
}
