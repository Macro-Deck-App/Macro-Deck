using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Actions;

namespace MacroDeckHost.Tests.UnitTests.Actions;

public class ActionParameterControlTypesTests
{
	// Mirrors mapActionParamType in desktop-ui action-parameter-mapping.util.ts.
	private static readonly Dictionary<ActionParameterType, string> _editorTypes = new()
	{
		[ActionParameterType.String] = "string",
		[ActionParameterType.Number] = "number",
		[ActionParameterType.Boolean] = "boolean",
		[ActionParameterType.Password] = "password",
		[ActionParameterType.Secret] = "secret",
		[ActionParameterType.Choice] = "choice",
		[ActionParameterType.DynamicChoice] = "dynamic-choice",
		[ActionParameterType.Autocomplete] = "autocomplete",
		[ActionParameterType.MultiSelect] = "multiselect",
		[ActionParameterType.Color] = "color",
		[ActionParameterType.File] = "file",
		[ActionParameterType.Folder] = "folder",
		[ActionParameterType.Hotkey] = "hotkey",
		[ActionParameterType.Duration] = "duration",
		[ActionParameterType.DateTime] = "datetime",
		[ActionParameterType.Json] = "json",
		[ActionParameterType.Code] = "code",
		[ActionParameterType.KeyValue] = "keyvalue",
		[ActionParameterType.Object] = "object",
		[ActionParameterType.Array] = "array",
		[ActionParameterType.IpAddress] = "ipaddress",
		[ActionParameterType.Url] = "url",
		[ActionParameterType.Icon] = "icon",
		[ActionParameterType.Image] = "image",
		[ActionParameterType.KeyboardSequence] = "keyboard-sequence",
		[ActionParameterType.KeyboardCombo] = "keyboard-combo",
		[ActionParameterType.WidgetTarget] = "widget-target"
	};

	[Test]
	public void Every_declared_type_converts_as_the_type_the_editor_stores_for_it()
	{
		Assert.Multiple(() =>
		{
			foreach (var type in Enum.GetValues<ActionParameterType>())
			{
				Assert.That(_editorTypes.ContainsKey(type), Is.True, $"{type} has no editor type");
				Assert.That(ActionParameterControlTypes.Of(type),
					Is.EqualTo(_editorTypes.GetValueOrDefault(type)),
					type.ToString());
			}
		});
	}

	[Test]
	public void A_missing_declared_parameter_is_filled_with_the_value_the_editor_fills_in()
	{
		var editorDefaults = EditorDefaultsFromTypeScript();

		Assert.Multiple(() =>
		{
			foreach (var type in Enum.GetValues<ActionParameterType>())
			{
				var controlType = ActionParameterControlTypes.Of(type);
				var expected = editorDefaults.GetValueOrDefault(controlType, editorDefaults["default"]);
				var actual = ActionParameterControlTypes.EditorDefault(new ActionParameter { Name = "p", Type = type });
				Assert.That(actual?.ToJsonString() ?? "null", Is.EqualTo(expected), controlType);
			}
		});
	}

	// Reads defaultParameterValue from ui/runtime itself, so a change on either side fails here.
	private static Dictionary<string, string> EditorDefaultsFromTypeScript()
	{
		var source = File.ReadAllText(Path.Combine(RepositoryRoot(),
			"ui",
			"runtime",
			"src",
			"domain",
			"action-flow.util.ts"));
		var body = source[source.IndexOf("export function defaultParameterValue", StringComparison.Ordinal)..];
		body = body[body.IndexOf("switch (p.type)", StringComparison.Ordinal)..];
		body = body[..body.IndexOf("\n}", StringComparison.Ordinal)];

		var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
		var pending = new List<string>();
		foreach (var line in body.Split('\n').Select(line => line.Trim()))
		{
			var caseMatch = Regex.Match(line, "^case '([^']+)':$");
			if (caseMatch.Success)
			{
				pending.Add(caseMatch.Groups[1].Value);
				continue;
			}

			if (line == "default:")
			{
				pending.Add("default");
				continue;
			}

			var returnMatch = Regex.Match(line, "^return (.+);$");
			if (returnMatch.Success)
			{
				foreach (var name in pending)
				{
					defaults[name] = ToJson(returnMatch.Groups[1].Value);
				}

				pending.Clear();
			}
		}

		return defaults;
	}

	private static string ToJson(string expression)
	{
		var literal = expression switch
		{
			"p.min ?? 0" => "0",
			_ when expression.Contains("p.options", StringComparison.Ordinal) => "''",
			_ => expression
		};
		literal = Regex.Replace(literal.Replace('\'', '"'), @"(\w+):", "\"$1\":");
		return JsonNode.Parse(literal)?.ToJsonString() ?? "null";
	}

	private static string RepositoryRoot()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MacroDeck.slnx")))
		{
			directory = directory.Parent;
		}

		return directory?.FullName ?? throw new InvalidOperationException("Repository root not found");
	}
}
