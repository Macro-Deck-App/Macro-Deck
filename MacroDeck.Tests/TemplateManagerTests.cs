using System.Collections;
using Cottle;
using SuchByte.MacroDeck.CottleIntegration;

namespace MacroDeck.Tests;

[TestFixture]
internal class TemplateManagerTests
{

    [Test]
    public void AllKeywords_Contains_NoNulls()
    {
        Assert.That(TemplateManager.AllKeywords, Has.No.Null.Or.Empty);
    }

    [Test]
    public void AllKeywords_Length_IsEqualTo_Content()
    {
        var contentLength = TemplateManager.AllKeywords.Count(x => !string.IsNullOrEmpty(x));
        Assert.That(TemplateManager.AllKeywords, Has.Length.EqualTo(contentLength));
    }

    [Test]
    public void AllKeywords_Length_IsEqualTo_ArrayLengths()
    {
        var expectedLength = TemplateManager.Commands.Length +
                             TemplateManager.Functions.Length +
                             TemplateManager.Operators.Length +
                             TemplateManager.Special.Length +
                             TemplateManager.MacroDeckFunctions.Length;
        Assert.That(TemplateManager.AllKeywords, Has.Length.EqualTo(expectedLength));
    }

    [TestCaseSource(typeof(TemplateManagerTestData), nameof(TemplateManagerTestData.Templates))]
    public string Render_Templates_NoDb(string template)
    {
        var doc = TemplateManager.GetDocument(template);
        return doc.Render(Context.CreateBuiltin(Context.Empty));
    }
    
}
public class TemplateManagerTestData
{
    public static IEnumerable Templates
    {
        get
        {
            yield return new TestCaseData("""
            { "these "}
            { "are "}
            { "words "}
            { "on "}
            { "new "}
            { "lines."}
            """)
                .Returns("these \r\nare \r\nwords \r\non \r\nnew \r\nlines."); //note spacing and new lines

            yield return new TestCaseData("""
            _trimblank_
            { "these "}
            { "are "}
            { "words "}
            { "on "}
            { "new "}
            { "lines."}
            """)
                .Returns("these are words on new lines.");
        }
    }
}
