using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Tests.UnitTests.Services;

public class HostBuildInfoTests
{
	[Test]
	public void ParseCommit_extracts_build_metadata()
	{
		Assert.That(HostBuildInfo.ParseCommit("1.2.3-beta.4+abc1234"), Is.EqualTo("abc1234"));
	}

	[Test]
	public void ParseCommit_keeps_only_the_short_hash_when_sdk_appends_full_revision()
	{
		Assert.That(HostBuildInfo.ParseCommit("3.0.0-beta.1+09e7daa.09e7daa933fbc48d1ea43cc28a010bea906ec75e"),
			Is.EqualTo("09e7daa"));
	}

	[Test]
	public void ParseCommit_shortens_a_lone_full_hash()
	{
		Assert.That(HostBuildInfo.ParseCommit("3.0.0+fc57639833df3ab9e5b350f9ad0340920757d601"),
			Is.EqualTo("fc57639"));
	}

	[Test]
	public void ParseCommit_leaves_non_hex_metadata_untouched()
	{
		Assert.That(HostBuildInfo.ParseCommit("1.2.3+local-build"), Is.EqualTo("local-build"));
	}

	[Test]
	public void ParseCommit_returns_null_without_metadata()
	{
		Assert.Multiple(() =>
		{
			Assert.That(HostBuildInfo.ParseCommit("1.2.3"), Is.Null);
			Assert.That(HostBuildInfo.ParseCommit("1.2.3+"), Is.Null);
			Assert.That(HostBuildInfo.ParseCommit("1.2.3+."), Is.Null);
			Assert.That(HostBuildInfo.ParseCommit(null), Is.Null);
			Assert.That(HostBuildInfo.ParseCommit("  "), Is.Null);
		});
	}
}
