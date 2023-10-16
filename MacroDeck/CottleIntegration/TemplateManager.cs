using System.Diagnostics;
using System.Reflection;
using Cottle;
using SuchByte.MacroDeck.Variables;

namespace SuchByte.MacroDeck.CottleIntegration;

public static class TemplateManager
{
    public const string TemplateTrimBlank = "_trimblank_";

    public static readonly string[] Operators = { "and", "cmp", "default", "defined", "eq", "ge", "gt", "has", "le", "lt", "ne",
        "not", "or", "xor", "when", "declare", "as", "dump", "echo", "empty", "set", "to",
        "return", "true", "false", "void"};
    public static readonly string[] Functions = { "abs", "add", "call", "cast", "cat", "ceil", "char", "cmp", "cos", "cross", "default", "defined",
        "div", "eq", "except", "filter", "find", "flip", "floor", "format", "ge", "gt", "has", "join", "lcase",
        "le", "len", "lt", "map", "match", "max", "min", "mod", "mul", "ne", "ord", "pow", "rand", "range", "round", "sin",
        "slice", "sort", "split", "sub", "token", "type", "ucase", "union", "when", "xor", "zip"};
    public static readonly string[] Commands = { "if", "elif", "else", "for", "while" };
    public static readonly string[] Special = { TemplateTrimBlank };

    public static bool HasTrimBlank(ReadOnlySpan<char> template) => template.StartsWith(TemplateTrimBlank, StringComparison.OrdinalIgnoreCase);
    
    private static string GetRenderTemplate(ReadOnlySpan<char> template, out DocumentConfiguration templateConfiguration)
    {
        templateConfiguration = new DocumentConfiguration
        {
            Trimmer = DocumentConfiguration.TrimNothing,
        };

        if (!template.StartsWith(TemplateTrimBlank, StringComparison.OrdinalIgnoreCase))
        {
            return template.ToString();
        }

        templateConfiguration.Trimmer = DocumentConfiguration.TrimFirstAndLastBlankLines;
        return template[TemplateTrimBlank.Length..].ToString();
    }

    public static IDocument GetDocument(ReadOnlySpan<char> template)
    { 
        var renderTemplate = GetRenderTemplate(template, out var configuration);
        return Document.CreateDefault(renderTemplate, configuration).DocumentOrThrow;
    }

    public static string RenderDocument(IDocument document)
    {
        var symbols = new Dictionary<Value, Value>();

        AddVariables(symbols);
        AddCustomFunctions(symbols);

        return document.Render(Context.CreateBuiltin(symbols));
    }

    public static string RenderTemplate(ReadOnlySpan<char> template)
    {
        if (template.IsEmpty)
        {
            return template.ToString();
        }
        string result;
        try
        {
            var document = GetDocument(template);
            result = RenderDocument(document);
        }
        catch (Exception e)
        {
            result = "Error: " + e.Message;
        }

        return result;
    }

    private static void AddVariables(IDictionary<Value, Value> symbols)
    {
        foreach (var v in VariableManager.ListVariables)
        {
            if (symbols.ContainsKey(v.Name)) continue;
            Value value = "";

            switch (v.Type)
            {
                case nameof(VariableType.Bool):
                    bool.TryParse(
                        v.Value.Replace("On", "True", StringComparison.CurrentCultureIgnoreCase)
                            .Replace("Off", "False", StringComparison.OrdinalIgnoreCase), out var resultBool);
                    value = Value.FromBoolean(resultBool);
                    break;
                case nameof(VariableType.Float):
                    float.TryParse(v.Value, out var resultFloat);
                    value = Value.FromNumber(resultFloat);
                    break;
                case nameof(VariableType.Integer):
                    int.TryParse(v.Value, out var resultInteger);
                    value = Value.FromNumber(resultInteger);
                    break;
                case nameof(VariableType.String):
                    value = Value.FromString(v.Value);
                    break;
            }

            symbols.Add(v.Name, value);
        }
    }

    private static void AddCustomFunctions(IDictionary<Value, Value> symbols)
    {
        foreach (var function in CustomFunctions)
        {
            symbols.Add(function.Key, function.Value);
        }

    }

    private static readonly Value GetDateTime = Value.FromFunction(
            Function.CreatePure1((state, formatString) => 
            //formatString.Type != ValueContent.String
            //    ? throw new InvalidCastException()
            //    : 
                DateTimeOffset.Now.ToString(formatString.AsString)
            )
        );

    private static readonly Value GetTimestamp = Value.FromFunction(Function.CreatePure0((state) => Stopwatch.GetTimestamp()));

    private static readonly Value GetTimerEnd = Value.FromLazy(() => Value.FromFunction(
        Function.CreatePure1((state, timestamp) =>
            Value.FromReflection(Stopwatch.GetElapsedTime((long)timestamp.AsNumber), BindingFlags.Instance | BindingFlags.Public)
        ))
    );

    private static readonly IDictionary<string, Value> CustomFunctions = new Dictionary<string, Value>
    {
        {"getdatetime", GetDateTime },
        {"gettimestamp", GetTimestamp },
        {"gettimerend", GetTimerEnd }
    };

    public static readonly string[] MacroDeckFunctions = CustomFunctions.Keys.ToArray();

    public static readonly string[] AllKeywords = GetAllKeywords();

    private static string[] GetAllKeywords()
    {
        string[] keywords = new string[Operators.Length + Functions.Length + Commands.Length + Special.Length + MacroDeckFunctions.Length];

        Copy(Operators);
        Copy(Functions);
        Copy(Commands);
        Copy(Special);
        Copy(MacroDeckFunctions);

        return keywords;

        void Copy(string[] array)
        {
            Array.Copy(array, 0,keywords, keywords.Count(x => !string.IsNullOrEmpty(x)), array.Length);
        }
    }
}
