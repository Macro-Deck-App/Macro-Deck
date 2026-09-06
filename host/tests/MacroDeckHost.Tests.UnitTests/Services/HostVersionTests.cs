using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class HostVersionTests
{
	[Test]
	public void Parse_strips_build_metadata()
	{
		Assert.That(HostVersion.Parse("1.2.3-beta.4+abc1234"), Is.EqualTo("1.2.3-beta.4"));
	}

	[Test]
	public void Parse_keeps_plain_semver()
	{
		Assert.That(HostVersion.Parse("1.2.3"), Is.EqualTo("1.2.3"));
	}

	[Test]
	public void Parse_falls_back_for_missing_version()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostVersion.Parse(null), Is.EqualTo(HostVersion.FallbackVersion));
			Assert.That(HostVersion.Parse("  "), Is.EqualTo(HostVersion.FallbackVersion));
		});
	}

	[Test]
	public void Current_is_never_empty()
	{
		Assert.That(HostVersion.Current, Is.Not.Empty);
	}

	[TestCase("3.0.0-beta.1", true)]
	[TestCase("3.0.0-beta.9", true)]
	[TestCase("3.0.0-beta.10", true)]
	[TestCase("3.0.0-beta.11", true)]
	[TestCase("3.0.0-beta.42+abc1234", true)]
	[TestCase("3.0.0", false)]
	[TestCase("3.0.0-b42", false)]
	[TestCase("3.0.0-beta.0", false)]
	[TestCase("3.0.0-beta.01", false)]
	[TestCase("3.0.0-beta.abc", false)]
	[TestCase("3.0.0-beta.1.preview", false)]
	[TestCase("3.0.0-Beta.1", false)]
	[TestCase("0.0.0-dev", false)]
	[TestCase("1.0.0", false)]
	[TestCase("", false)]
	[TestCase(null, false)]
	public void IsBetaVersion_detects_the_beta_pre_release_tag(string? version, bool expected)
	{
		Assert.That(HostVersion.IsBetaVersion(version), Is.EqualTo(expected));
	}
}
