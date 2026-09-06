using MacroDeckHost.Infrastructure.Plugins;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class DotnetMuxerLocatorTests
{
	[Test]
	public void Only_the_shared_runtime_lines_are_parsed()
	{
		const string output = """
							  Microsoft.AspNetCore.App 8.0.11 [/usr/share/dotnet/shared/Microsoft.AspNetCore.App]
							  Microsoft.NETCore.App 8.0.11 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
							  Microsoft.NETCore.App 10.0.0 [/usr/share/dotnet/shared/Microsoft.NETCore.App]
							  Microsoft.WindowsDesktop.App 8.0.11 [C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App]
							  """;

		Assert.That(DotnetMuxerLocator.ParseInstalledRuntimes(output),
			Is.EqualTo(new[] { new Version(8, 0, 11), new Version(10, 0, 0) }));
	}

	[Test]
	public void A_prerelease_runtime_is_read_by_its_numeric_core()
	{
		const string output =
			"Microsoft.NETCore.App 10.0.0-rc.1.24001.1 [/usr/share/dotnet/shared/Microsoft.NETCore.App]";

		Assert.That(DotnetMuxerLocator.ParseInstalledRuntimes(output),
			Is.EqualTo(new[] { new Version(10, 0, 0) }));
	}

	[Test]
	public void Malformed_and_empty_output_yields_no_runtimes_rather_than_throwing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DotnetMuxerLocator.ParseInstalledRuntimes(string.Empty), Is.Empty);
			Assert.That(DotnetMuxerLocator.ParseInstalledRuntimes("not a runtime line"), Is.Empty);
			Assert.That(DotnetMuxerLocator.ParseInstalledRuntimes("Microsoft.NETCore.App nonsense"), Is.Empty);
		});
	}

	[Test]
	public void Duplicate_runtime_versions_are_collapsed()
	{
		const string output = """
							  Microsoft.NETCore.App 10.0.0 [/a]
							  Microsoft.NETCore.App 10.0.0 [/b]
							  """;

		Assert.That(DotnetMuxerLocator.ParseInstalledRuntimes(output), Has.Count.EqualTo(1));
	}

	[Test]
	public void A_matching_major_with_a_high_enough_minor_satisfies_the_requirement()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied("10.0", [new Version(10, 0, 3)]), Is.True);
			Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied("10.0", [new Version(10, 2, 0)]), Is.True);
		});
	}

	[Test]
	public void A_different_major_never_satisfies_the_requirement()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied("10.0", [new Version(11, 0, 0)]), Is.False);
			Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied("10.0", [new Version(8, 0, 0)]), Is.False);
		});
	}

	[Test]
	public void A_lower_minor_of_the_right_major_does_not_satisfy_the_requirement()
	{
		Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied("10.2", [new Version(10, 1, 0)]), Is.False);
	}

	[Test]
	public void Nothing_installed_satisfies_nothing()
	{
		Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied("10.0", []), Is.False);
	}

	[TestCase("")]
	[TestCase("ten")]
	[TestCase("10")]
	[TestCase("10.0.0.0.0")]
	public void A_malformed_requirement_is_never_satisfied(string requirement)
	{
		Assert.That(DotnetMuxerLocator.IsRuntimeSatisfied(requirement, [new Version(10, 0, 0)]), Is.False);
	}
}
