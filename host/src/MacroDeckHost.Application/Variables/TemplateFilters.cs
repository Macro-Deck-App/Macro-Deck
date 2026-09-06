using System.Text;
using Scriban.Runtime;

namespace MacroDeckHost.Application.Variables;

internal static class TemplateFilters
{
	internal const string PlainTextName = "plain_text";

	private static readonly IScriptCustomFunction _plainText =
		DelegateCustomFunction.CreateFunc<string?, string>(PlainText);

	internal static string PlainText(string? value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		try
		{
			return value.Normalize(NormalizationForm.FormKC);
		}
		catch (ArgumentException)
		{
			return value;
		}
	}

	internal static ScriptObject CreateScope() => new() { [PlainTextName] = _plainText };
}
