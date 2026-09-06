using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace MacroDeck.Plugin.Analyzers.Tests.UnitTests;

/// <summary>
/// The "analyzer diagnostics use stable IDs and include actionable messages" acceptance criterion, made
/// mechanical: every descriptor this assembly reports has a stable <c>MDPnnnn</c> or
/// <c>MDLOCnnn</c> id, a
/// non-empty title and message, and a help link.
/// </summary>
[TestFixture]
public class DiagnosticDescriptorTests
{
	// Two families, both stable: MDPnnnn for the plugin analyzers, MDLOCnnn for the localization
	// compiler, which is a separate toolchain with its own resource inputs and whose ids issue #326
	// specifies verbatim.
	private static readonly Regex _stableIdPattern = new(@"\A(MDP\d{4}|MDLOC\d{3})\z");

	/// <summary>
	/// Every <see cref="DiagnosticDescriptor" /> field this package declares, found by reflection rather
	/// than a hand-maintained list, so a rule added later is checked automatically instead of only when
	/// someone remembers to extend this list too.
	/// </summary>
	private static IEnumerable<DiagnosticDescriptor> AllDescriptors()
		=> typeof(DiagnosticDescriptors)
			.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			.Where(field => field.FieldType == typeof(DiagnosticDescriptor))
			.Select(field => (DiagnosticDescriptor)field.GetValue(null)!);

	[Test]
	public void At_least_thirteen_descriptors_are_declared()
	{
		// A basic sanity check that reflection actually found the descriptors, not an empty set that
		// would make every other test in this fixture vacuously pass.
		Assert.That(AllDescriptors().Count(), Is.GreaterThanOrEqualTo(13));
	}

	[Test]
	public void Every_descriptor_has_a_stable_id_a_title_a_message_and_a_help_link()
	{
		Assert.Multiple(() =>
		{
			foreach (var descriptor in AllDescriptors())
			{
				Assert.That(_stableIdPattern.IsMatch(descriptor.Id),
					Is.True,
					$"{descriptor.Id}: id is neither MDPnnnn nor MDLOCnnn");
				Assert.That(descriptor.Title.ToString(CultureInfo.InvariantCulture),
					Is.Not.Empty,
					$"{descriptor.Id}: empty title");
				Assert.That(descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture),
					Is.Not.Empty,
					$"{descriptor.Id}: empty message format");
				Assert.That(descriptor.HelpLinkUri, Is.Not.Null.And.Not.Empty, $"{descriptor.Id}: empty help link");
				Assert.That(descriptor.Category, Is.Not.Empty, $"{descriptor.Id}: empty category");
			}
		});
	}

	[Test]
	public void No_two_descriptors_share_an_id()
	{
		var ids = AllDescriptors().Select(descriptor => descriptor.Id).ToList();

		Assert.That(ids, Is.Unique);
	}
}
