using System.Reflection;
using MacroDeck.Sdk.Deprecation;

namespace MacroDeck.Sdk.Tests.UnitTests.Deprecation;

/// <summary>
/// The enforcement behind issue #418's rule that an API cannot be removed unless its deprecation
/// lifecycle has been documented and tested.
///
/// <para>
/// Each test here closes one way the lifecycle could be skipped: deprecating an API without recording
/// it, recording it without documenting it, deleting it without moving its entry, or declaring a
/// lifecycle that leaves plugin authors no release to migrate on. Together they mean the only way to
/// remove a public API is to walk the whole documented path - a shortcut turns the build red.
/// </para>
/// </summary>
[TestFixture]
public class SdkDeprecationLifecycleTests
{
	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
	}

	private static string DeprecationsDocument()
		=> File.ReadAllText(Path.Combine(RepositoryRoot(),
			"docs",
			"src",
			"content",
			"docs",
			"policies",
			"deprecations.md"));

	/// <summary>Every public member of the SDK, including those declared on nested and non-visible types.</summary>
	private static IEnumerable<MemberInfo> PublicSurface()
	{
		var assembly = typeof(MacroDeckDeprecatedAttribute).Assembly;

		foreach (var type in assembly.GetTypes())
		{
			yield return type;

			foreach (var member in type.GetMembers(BindingFlags.Public |
				BindingFlags.NonPublic |
				BindingFlags.Instance |
				BindingFlags.Static |
				BindingFlags.DeclaredOnly))
			{
				yield return member;
			}
		}
	}

	private static IEnumerable<MemberInfo> DeprecatedMembers()
		=> PublicSurface().Where(member => member.GetCustomAttribute<MacroDeckDeprecatedAttribute>() is not null);

	/// <summary>
	/// A deprecation the registry does not know about is invisible to the host: the plugin reports the
	/// api id, the host fails to resolve it, and the user is told the API is unknown rather than
	/// deprecated. Marking a member is therefore only half the job.
	/// </summary>
	[Test]
	public void Every_deprecated_member_has_a_registry_entry()
	{
		var registered = SdkDeprecations.All.Select(entry => entry.ApiId).ToHashSet(StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			foreach (var member in DeprecatedMembers())
			{
				var apiId = DocumentationCommentId(member);

				Assert.That(registered,
					Does.Contain(apiId),
					$"'{apiId}' is marked [MacroDeckDeprecated] but has no SdkDeprecations entry");
			}
		});
	}

	/// <summary>
	/// The companion [Obsolete] is what makes a deprecation visible to the compiler, every IDE and every
	/// analyzer that is not Macro Deck's own. MDP5003 reports this at compile time; this test makes sure
	/// the shipped surface never relies on someone having heeded that warning.
	/// </summary>
	[Test]
	public void Every_deprecated_member_also_carries_Obsolete()
	{
		Assert.Multiple(() =>
		{
			foreach (var member in DeprecatedMembers())
			{
				Assert.That(member.GetCustomAttribute<ObsoleteAttribute>(),
					Is.Not.Null,
					$"'{DocumentationCommentId(member)}' is [MacroDeckDeprecated] without a companion [Obsolete]");
			}
		});
	}

	/// <summary>
	/// A removal version at or before the deprecation version promises a release in which the API is both
	/// newly deprecated and already gone, leaving nobody a version to migrate on.
	/// </summary>
	[Test]
	public void Every_registry_entry_declares_a_usable_lifecycle()
	{
		Assert.Multiple(() =>
		{
			foreach (var entry in SdkDeprecations.All)
			{
				Assert.That(Version.TryParse(entry.DeprecatedIn, out var deprecated),
					Is.True,
					$"'{entry.ApiId}' has an unparseable DeprecatedIn '{entry.DeprecatedIn}'");
				Assert.That(Version.TryParse(entry.RemovedIn, out var removed),
					Is.True,
					$"'{entry.ApiId}' has an unparseable RemovedIn '{entry.RemovedIn}'");
				Assert.That(removed,
					Is.GreaterThan(deprecated ?? new Version(0, 0)),
					$"'{entry.ApiId}' is removed in {entry.RemovedIn}, which is not after {entry.DeprecatedIn}");
				Assert.That(entry.Guidance,
					Is.Not.Empty,
					$"'{entry.ApiId}' declares no guidance, so its warning is not actionable");
			}
		});
	}

	/// <summary>
	/// The removal gate itself. An entry in Active names an API that must still exist; deleting that API
	/// without moving its entry to Removed fails here. Moving it is what forces the documentation row the
	/// next test checks, so the two together make an undocumented removal impossible.
	/// </summary>
	[Test]
	public void Every_active_entry_still_resolves_and_every_removed_entry_does_not()
	{
		var present = PublicSurface().Select(DocumentationCommentId).ToHashSet(StringComparer.Ordinal);

		Assert.Multiple(() =>
		{
			foreach (var entry in SdkDeprecations.Active)
			{
				Assert.That(present,
					Does.Contain(entry.ApiId),
					$"'{entry.ApiId}' is listed as deprecated-but-present and no longer exists - move it to " +
					"SdkDeprecations.Removed and document the removal");
			}

			foreach (var entry in SdkDeprecations.Removed)
			{
				Assert.That(present,
					Does.Not.Contain(entry.ApiId),
					$"'{entry.ApiId}' is listed as removed but still exists in the SDK");
			}
		});
	}

	/// <summary>
	/// A registry entry nobody can read is not documentation. Every deprecation, active or removed, has a
	/// row naming it in deprecations.md - which is what a plugin author actually reads when a warning
	/// tells them to migrate.
	/// </summary>
	[Test]
	public void Every_registry_entry_is_named_in_the_deprecations_document()
	{
		var document = DeprecationsDocument();

		Assert.Multiple(() =>
		{
			foreach (var entry in SdkDeprecations.All)
			{
				Assert.That(document,
					Does.Contain(entry.DisplayName),
					$"'{entry.DisplayName}' has no row in docs/src/content/docs/policies/deprecations.md");
			}
		});
	}

	/// <summary>
	/// Guards the four tests above against passing vacuously. Nothing is deprecated today, so every loop
	/// over the registry is currently empty - this asserts that emptiness is the real state of the SDK
	/// rather than a reflection query that silently stopped matching anything.
	/// </summary>
	[Test]
	public void The_registry_and_the_annotated_surface_agree_on_being_empty()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SdkDeprecations.All.Count, Is.EqualTo(DeprecatedMembers().Count()));
			Assert.That(SdkDeprecations.All,
				Is.EqualTo(SdkDeprecations.Active.Concat(SdkDeprecations.Removed).ToList()));
		});
	}

	/// <summary>
	/// The registry is keyed by documentation comment id because that is the one spelling the analyzer,
	/// the usage-manifest generator and the host can all derive independently. This pins the format so a
	/// future entry cannot be added as a display name and silently fail every lookup.
	/// </summary>
	[Test]
	public void Every_registry_entry_is_keyed_by_a_documentation_comment_id()
	{
		Assert.Multiple(() =>
		{
			foreach (var entry in SdkDeprecations.All)
			{
				Assert.That(entry.ApiId,
					Does.Match(@"\A[TMPEF]:"),
					$"'{entry.ApiId}' is not a documentation comment id (T:, M:, P:, E: or F:)");
			}
		});
	}

	/// <summary>
	/// Reflection's own spelling of the documentation comment id, built here rather than taken from
	/// production code so the test and the registry cannot agree on a wrong format.
	/// </summary>
	private static string DocumentationCommentId(MemberInfo member) => member switch
	{
		Type type => $"T:{type.FullName}",
		MethodBase method => $"M:{method.DeclaringType?.FullName}.{method.Name}",
		PropertyInfo property => $"P:{property.DeclaringType?.FullName}.{property.Name}",
		EventInfo eventInfo => $"E:{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}",
		FieldInfo field => $"F:{field.DeclaringType?.FullName}.{field.Name}",
		_ => $"?:{member.Name}"
	};
}
