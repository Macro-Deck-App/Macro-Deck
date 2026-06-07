using System.Globalization;
using SuchByte.MacroDeck.Variables;

namespace MacroDeck.Tests;

/// <summary>
/// Tests the variable-name normalization that plugins rely on (lower-casing, replacing separators,
/// transliterating umlauts). Stable behaviour here matters because the result is used as a key.
/// </summary>
[TestFixture]
public class ConvertNameStringTests
{
    private CultureInfo _originalCulture = null!;

    [SetUp]
    public void SetUp()
    {
        _originalCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TearDown]
    public void TearDown()
    {
        Thread.CurrentThread.CurrentCulture = _originalCulture;
    }

    [TestCase("My Variable", ExpectedResult = "my_variable")]
    [TestCase("already_clean", ExpectedResult = "already_clean")]
    [TestCase("a.b-c/d|e", ExpectedResult = "a_b_c_d_e")]
    [TestCase("MIXED Case", ExpectedResult = "mixed_case")]
    [TestCase("Über Straße", ExpectedResult = "ueber_strasse")]
    [TestCase("ÄÖÜ", ExpectedResult = "aeoeue")]
    public string ConvertNameString_NormalizesNames(string input)
    {
        return VariableManager.ConvertNameString(input);
    }
}
