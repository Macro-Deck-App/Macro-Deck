using MacroDeck.Plugin.Testing.Conformance;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

/// <summary>Static properties of the check catalogue itself, independent of any subject or report.</summary>
[TestFixture]
internal sealed class CheckCatalogTests
{
	private static IReadOnlyList<IConformanceCheck> Checks => new ConformanceRunner().Checks;

	[Test]
	public void CheckIdsAreUnique()
		=> Assert.That(Checks.Select(check => check.Id), Is.Unique);

	[Test]
	public void EveryCheckHasANonEmptyTitle()
	{
		foreach (var check in Checks)
		{
			Assert.That(check.Title, Is.Not.Null.And.Not.Empty, $"{check.Id} has no title.");
		}
	}

	[Test]
	public void EveryCheckIdMatchesItsDeclaredCategory()
	{
		var categoryNumbers = new Dictionary<ConformanceCategory, string>
		{
			[ConformanceCategory.ManifestAndIdentifiers] = "01",
			[ConformanceCategory.RegistrationAndNegotiation] = "02",
			[ConformanceCategory.CapabilitySerialization] = "03",
			[ConformanceCategory.DuplicateIds] = "04",
			[ConformanceCategory.TimeoutAndCancellation] = "05",
			[ConformanceCategory.DisconnectAndReconnect] = "06",
			[ConformanceCategory.HealthEndpoint] = "07",
			[ConformanceCategory.BoundedQueues] = "08"
		};

		foreach (var check in Checks)
		{
			var expectedPrefix = $"MDC{categoryNumbers[check.Category]}";

			Assert.That(check.Id,
				Does.StartWith(expectedPrefix),
				$"{check.Id} is categorised as {check.Category} but its id does not start with '{expectedPrefix}'.");
		}
	}

	[Test]
	public void EveryCheckHasAtLeastOnePrecondition()
	{
		// Not a hard protocol requirement, but every check in this suite happens to declare at least
		// Session - a sanity check that Requires was not simply left empty by accident.
		foreach (var check in Checks)
		{
			Assert.That(check.Requires, Is.Not.Empty, $"{check.Id} declares no preconditions at all.");
		}
	}

	[Test]
	public void ThereAreFortyNineChecksAcrossEightCategories()
	{
		Assert.That(Checks, Has.Count.EqualTo(49));
		Assert.That(Checks.Select(check => check.Category).Distinct().ToList(), Has.Count.EqualTo(8));
	}
}
