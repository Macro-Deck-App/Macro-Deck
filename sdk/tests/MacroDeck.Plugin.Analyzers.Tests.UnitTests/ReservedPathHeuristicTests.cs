using MacroDeck.Plugin.Hosting.Endpoints;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// The analyzer hardcodes a copy of ReservedPaths.IsReserved's logic rather than linking or referencing
/// it (see ReservedPathHeuristic's remarks). This checks the copy agrees with the real thing across a
/// representative battery of paths, the same drift-prevention idea as the capability-kind vocabulary test.
/// </summary>
[TestFixture]
public class ReservedPathHeuristicTests
{
	private static readonly string?[] _paths =
	[
		"/_macrodeck",
		"/_macrodeck/",
		"/_macrodeck/health",
		"/_macrodeck/ready",
		"/_macrodeck/info",
		"/_macrodeck/diagnostics",
		"/_macrodeckery",
		"_macrodeck/health",
		"/my-plugin/status",
		"/",
		"",
		null,
		"/_MACRODECK/HEALTH"
	];

	[Test]
	public void Agrees_with_ReservedPaths_IsReserved_across_a_battery_of_paths()
	{
		Assert.Multiple(() =>
		{
			foreach (var path in _paths)
			{
				Assert.That(ReservedPathHeuristic.IsReserved(path),
					Is.EqualTo(ReservedPaths.IsReserved(path)),
					$"path: {path ?? "<null>"}");
			}
		});
	}
}
