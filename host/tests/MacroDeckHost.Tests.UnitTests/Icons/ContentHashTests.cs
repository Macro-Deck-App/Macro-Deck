using System.Text;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class ContentHashTests
{
	// The hash of "abc", so a change in algorithm or encoding cannot pass silently.
	private const string _abcHash = "sha256:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";

	[Test]
	public void Compute_WritesTheAlgorithmAndLowercaseHex()
	{
		Assert.That(ContentHash.Compute("abc"u8), Is.EqualTo(_abcHash));
	}

	[Test]
	public void CreateIncremental_MatchesHashingInOneGo()
	{
		var content = Encoding.UTF8.GetBytes(new string('x', 200_000));

		using var incremental = ContentHash.CreateIncremental();
		foreach (var chunk in content.Chunk(4096))
		{
			incremental.Append(chunk);
		}

		Assert.That(incremental.Finish(), Is.EqualTo(ContentHash.Compute(content)));
	}

	[Test]
	public void Normalize_AcceptsTheLegacyBareHexForm()
	{
		Assert.That(ContentHash.Normalize("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD"),
			Is.EqualTo(_abcHash));
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	[TestCase("abc")]
	[TestCase("sha256:abc")]
	[TestCase("md5:ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	[TestCase("zz7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")]
	public void Normalize_RejectsAnythingThatIsNotASha256(string? value)
	{
		Assert.That(ContentHash.Normalize(value), Is.Null);
	}

	[Test]
	public void SourceAndMasterHashes_OfTheSameBytes_AreNotInterchangeable()
	{
		var source = SourceContentHash.Compute("abc"u8);
		var master = MasterContentHash.Compute("abc"u8);

		// The strings are equal - these hash the same bytes - but the types are not assignable to each
		// other, which is what stops a source hash from ever being looked up as a master hash. That is a
		// compile-time guarantee; this only pins the values so a refactor cannot quietly merge the types.
		Assert.Multiple(() =>
		{
			Assert.That(source.Value, Is.EqualTo(_abcHash));
			Assert.That(master.Value, Is.EqualTo(_abcHash));
			Assert.That(typeof(SourceContentHash), Is.Not.EqualTo(typeof(MasterContentHash)));
		});
	}

	[Test]
	public void TryParse_RoundTripsAStoredValue()
	{
		Assert.Multiple(() =>
		{
			Assert.That(SourceContentHash.TryParse(_abcHash, out var source), Is.True);
			Assert.That(source.Value, Is.EqualTo(_abcHash));
			Assert.That(MasterContentHash.TryParse("not a hash", out _), Is.False);
		});
	}
}
