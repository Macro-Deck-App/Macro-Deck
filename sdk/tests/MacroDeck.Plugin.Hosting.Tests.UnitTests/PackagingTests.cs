using System.Xml.Linq;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// Reads the project files as XML, the way the protocol's own contract-isolation test does.
/// Reflection cannot answer these questions: a package id and a readme are build inputs, not
/// something the compiled assembly remembers.
/// </summary>
[TestFixture]
public class PackagingTests
{
	private static readonly string[] _packableProjects =
	[
		"sdk/src/MacroDeck.Sdk/MacroDeck.Sdk.csproj",
		"sdk/src/MacroDeck.Plugin.Hosting/MacroDeck.Plugin.Hosting.csproj",
		"sdk/src/MacroDeck.Plugin.Serilog/MacroDeck.Plugin.Serilog.csproj",
		"sdk/src/MacroDeck.Plugin.Packaging/MacroDeck.Plugin.Packaging.csproj",
		"sdk/src/MacroDeck.Plugin.Testing/MacroDeck.Plugin.Testing.csproj",
		"sdk/src/MacroDeck.Plugin.Analyzers/MacroDeck.Plugin.Analyzers.csproj",
		"sdk/src/MacroDeck.Plugin.Cli/MacroDeck.Plugin.Cli.csproj",
		"protocol/src/MacroDeck.Plugin.Protocol/MacroDeck.Plugin.Protocol.csproj"
	];

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);

		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
	}

	private static XDocument Load(string relativePath)
		=> XDocument.Load(Path.Combine(RepositoryRoot(), relativePath));

	private static string? Property(XDocument project, string name)
		=> project.Descendants(name).FirstOrDefault()?.Value;

	[Test]
	public void Every_published_package_declares_an_id_matching_its_assembly_name()
	{
		Assert.Multiple(() =>
		{
			foreach (var path in _packableProjects)
			{
				var project = Load(path);
				var expected = Path.GetFileNameWithoutExtension(path);

				Assert.That(Property(project, "IsPackable"), Is.EqualTo("true"), path);
				Assert.That(Property(project, "PackageId"), Is.EqualTo(expected), path);
				Assert.That(Property(project, "Description"), Is.Not.Empty, path);
			}
		});
	}

	[Test]
	public void Every_published_package_opts_into_the_shared_metadata()
	{
		Assert.Multiple(() =>
		{
			foreach (var path in _packableProjects)
			{
				Assert.That(Property(Load(path), "IsMacroDeckPackage"), Is.EqualTo("true"), path);
			}
		});
	}

	[Test]
	public void Every_published_package_ships_the_readme_it_declares()
	{
		// PackageReadmeFile without a packed file is NU5039, which fails the pack rather than warning.
		Assert.Multiple(() =>
		{
			foreach (var path in _packableProjects)
			{
				var readme = Path.Combine(RepositoryRoot(), Path.GetDirectoryName(path)!, "README.md");
				Assert.That(File.Exists(readme), Is.True, readme);
			}
		});
	}

	[Test]
	public void The_hosting_package_takes_the_framework_rather_than_packages()
	{
		var project = Load("sdk/src/MacroDeck.Plugin.Hosting/MacroDeck.Plugin.Hosting.csproj");

		Assert.Multiple(() =>
		{
			// A package reference here would need a central version entry and would become a public
			// dependency of every plugin; the shared framework costs consumers nothing extra.
			Assert.That(project.Descendants("PackageReference"), Is.Empty);
			Assert.That(project.Descendants("FrameworkReference")
					.Select(reference => reference.Attribute("Include")?.Value),
				Does.Contain("Microsoft.AspNetCore.App"));
		});
	}

	[Test]
	public void The_shared_packaging_metadata_names_the_repository_licence()
	{
		var targets = Load("Directory.Build.targets");

		Assert.That(Property(targets, "PackageLicenseExpression"), Is.EqualTo("Apache-2.0"));
	}
}
