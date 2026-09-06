using System.Reflection;
using Microsoft.CodeAnalysis;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// RS2007/RS2008 (build-time, via EnforceExtendedAnalyzerRules) catch an unshipped or missing release
/// entry; they do not catch a stale one where the file's own Category or Severity column has drifted from
/// the descriptor's actual value (RS2001 does catch that - see DiagnosticDescriptors's history - but nothing
/// stops the *next* drift from slipping through unnoticed if this test is ever deleted).
/// </summary>
[TestFixture]
public class AnalyzerReleasesTests
{
	private static IEnumerable<DiagnosticDescriptor> AllDescriptors()
		=> typeof(DiagnosticDescriptors)
			.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(DiagnosticDescriptor))
			.Select(field => (DiagnosticDescriptor)field.GetValue(null)!);

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
	}

	private static string[] ShippedFileLines()
		=> File.ReadAllLines(Path.Combine(RepositoryRoot(),
			"sdk",
			"src",
			"MacroDeck.Plugin.Analyzers",
			"AnalyzerReleases.Shipped.md"));

	private static string[] UnshippedFileLines()
		=> File.ReadAllLines(Path.Combine(RepositoryRoot(),
			"sdk",
			"src",
			"MacroDeck.Plugin.Analyzers",
			"AnalyzerReleases.Unshipped.md"));

	/// <summary>
	/// Only the "### New Rules" table's rows - the ones this test can hold to the same
	/// "Id | Category | Severity | Notes" shape the shipped file's rows have. Unshipped.md's own
	/// "### Changed Rules" table (if present) has a different column shape entirely (New/Old
	/// Category/Severity), and a "; comment" line is not a row at all - both are skipped by requiring an
	/// "MDP" id at the start of the line, same as <see cref="ShippedFileLines" />'s own filtering.
	/// </summary>
	/// <summary>Whether a line is a rule row rather than a comment or a differently shaped table.
	/// Both id families count: MDPnnnn for the analyzers, MDLOCnnn for the localization compiler.</summary>
	private static bool IsRuleRow(string line)
		=> line.StartsWith("MDP", StringComparison.Ordinal) ||
			line.StartsWith("MDLOC", StringComparison.Ordinal);

	private static IEnumerable<string> UnshippedNewRuleLines()
		=> UnshippedFileLines().Where(IsRuleRow);

	[Test]
	public void
		Every_descriptor_is_listed_in_the_shipped_or_unshipped_release_file_with_a_matching_category_and_severity()
	{
		// A descriptor is either already shipped, or new-this-release and only in Unshipped.md's "New
		// Rules" table - never neither, and (RS2007 already guarantees this at build time) never both.
		var shippedLines = ShippedFileLines();
		var unshippedLines = UnshippedNewRuleLines().ToArray();

		Assert.Multiple(() =>
		{
			foreach (var descriptor in AllDescriptors())
			{
				var row = shippedLines.FirstOrDefault(line =>
						line.StartsWith(descriptor.Id + " ", StringComparison.Ordinal)) ??
					unshippedLines.FirstOrDefault(line =>
						line.StartsWith(descriptor.Id + " ", StringComparison.Ordinal));

				Assert.That(row,
					Is.Not.Null,
					$"{descriptor.Id} is not listed in AnalyzerReleases.Shipped.md or AnalyzerReleases.Unshipped.md");

				var columns = row!.Split('|').Select(column => column.Trim()).ToArray();

				Assert.That(columns, Has.Length.GreaterThanOrEqualTo(3), $"{descriptor.Id}: malformed row '{row}'");
				Assert.That(columns[1], Is.EqualTo(descriptor.Category), $"{descriptor.Id}: category column is stale");
				Assert.That(columns[2],
					Is.EqualTo(descriptor.DefaultSeverity.ToString()),
					$"{descriptor.Id}: severity column is stale");
			}
		});
	}

	[Test]
	public void The_shipped_and_unshipped_release_files_list_no_id_that_is_not_a_real_descriptor()
	{
		var knownIds = AllDescriptors().Select(descriptor => descriptor.Id).ToHashSet(StringComparer.Ordinal);

		var listedIds = ShippedFileLines()
			.Where(IsRuleRow)
			.Select(line => line.Split('|')[0].Trim())
			.Concat(UnshippedNewRuleLines().Select(line => line.Split('|')[0].Trim()));

		Assert.That(listedIds, Is.SubsetOf(knownIds));
	}
}
