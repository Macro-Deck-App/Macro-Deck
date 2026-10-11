using System.Reflection;
using MacroDeck.Sdk;
using MacroDeckHost.Integrations;

namespace MacroDeckHost.Tests.UnitTests.Architecture;

// ADR 0107: a built-in integration must stay extractable into an out-of-process plugin. The baseline
// lists the violations that predate the rule; it may only shrink.
[TestFixture]
internal sealed class IntegrationBoundaryTests
{
	private const string IntegrationsNamespace = "MacroDeckHost.Integrations";

	private static readonly string[] _hostAssemblies =
	[
		"MacroDeckHost.Application",
		"MacroDeckHost.Domain",
		"MacroDeckHost.Infrastructure",
		"MacroDeckHost.Widgets",
		"MacroDeckHost",
	];

	private static readonly Assembly _integrations = typeof(IntegrationDiscovery).Assembly;

	[Test]
	public void An_integration_uses_no_host_types()
	{
		var hostAssemblies = _hostAssemblies.Select(Assembly.Load).ToHashSet();
		var systemFolders = SystemIntegrationFolders();

		var actual = AssemblyDependencies.TypesOf(_integrations)
			.Select(type => (Folder: FolderOf(type), Type: type))
			.Where(entry => entry.Folder is null || !systemFolders.Contains(entry.Folder))
			.SelectMany(entry => AssemblyDependencies.ReferencedTypes(entry.Type)
				.Where(used => hostAssemblies.Contains(used.Assembly))
				.Select(used => $"integration {entry.Folder ?? "(shared)"} uses {AssemblyDependencies.Outermost(used).FullName}"));

		AssertMatchesBaseline("integration ", actual);
	}

	[Test]
	public void Host_code_uses_no_types_of_a_specific_integration()
	{
		var systemFolders = SystemIntegrationFolders();

		var actual = HostTypes()
			.SelectMany(type => AssemblyDependencies.ReferencedTypes(type)
				.Where(used => used.Assembly == _integrations)
				.Select(FolderOf)
				.OfType<string>()
				.Where(folder => !systemFolders.Contains(folder))
				.Select(folder => $"host {AssemblyDependencies.Outermost(type).FullName} uses {folder}"));

		AssertMatchesBaseline("host ", actual);
	}

	[Test]
	public void Host_code_names_no_specific_integration()
	{
		var ids = NonSystemIntegrationIds();

		var actual = HostTypes()
			.SelectMany(type => AssemblyDependencies.StringLiterals(type)
				.SelectMany(literal => ids.Where(id => literal == id || literal.StartsWith(id + "::", StringComparison.Ordinal)))
				.Select(id => $"names {AssemblyDependencies.Outermost(type).FullName} {id}"));

		AssertMatchesBaseline("names ", actual);
	}

	private static IEnumerable<Type> HostTypes()
		=> _hostAssemblies.Select(Assembly.Load).SelectMany(AssemblyDependencies.TypesOf);

	private static string? FolderOf(Type type)
	{
		var ns = AssemblyDependencies.Outermost(type).Namespace ?? string.Empty;
		if (!ns.StartsWith(IntegrationsNamespace + ".", StringComparison.Ordinal))
		{
			return null;
		}

		return ns[(IntegrationsNamespace.Length + 1)..].Split('.')[0];
	}

	private static IEnumerable<Type> IntegrationTypes()
		=> AssemblyDependencies.TypesOf(_integrations).Where(type => type is { IsClass: true, IsAbstract: false } &&
			type.GetCustomAttribute<MacroDeckIntegrationAttribute>() is not null &&
			typeof(IIntegration).IsAssignableFrom(type));

	private static HashSet<string> SystemIntegrationFolders()
		=> IntegrationTypes()
			.Where(type => typeof(ISystemIntegration).IsAssignableFrom(type))
			.Select(FolderOf)
			.OfType<string>()
			.ToHashSet(StringComparer.Ordinal);

	private static List<string> NonSystemIntegrationIds()
		=> IntegrationTypes()
			.Where(type => !typeof(ISystemIntegration).IsAssignableFrom(type))
			.Select(type => ((IIntegration)Activator.CreateInstance(type)!).Id)
			.Distinct(StringComparer.Ordinal)
			.ToList();

	private static void AssertMatchesBaseline(string prefix, IEnumerable<string> actual)
	{
		var found = actual.ToHashSet(StringComparer.Ordinal);
		var baseline = Baseline().Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal);

		var added = found.Except(baseline).Order(StringComparer.Ordinal).ToList();
		var resolved = baseline.Except(found).Order(StringComparer.Ordinal).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(added, Is.Empty,
				"New coupling between the host and a specific built-in integration. Use the SDK instead (ADR 0107):"
				+ Environment.NewLine + string.Join(Environment.NewLine, added));
			Assert.That(resolved, Is.Empty,
				"These baseline entries no longer occur. Remove them from IntegrationBoundary.baseline:"
				+ Environment.NewLine + string.Join(Environment.NewLine, resolved));
		});
	}

	private static List<string> Baseline()
	{
		using var stream = typeof(IntegrationBoundaryTests).Assembly.GetManifestResourceStream("IntegrationBoundary.baseline")!;
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd()
			.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.ToList();
	}
}
