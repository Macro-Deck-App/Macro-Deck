using System.Text.Json;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using SdkParameterType = MacroDeck.Sdk.Actions.ActionParameterType;
using TransportParameterType = MacroDeckHost.Application.Ui.Transport.Messages.Actions.ActionParameterType;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Widgets;

[TestFixture]
public class LaunchApplicationButtonJsonTests
{
	private static readonly Dictionary<TransportParameterType, string> _clientControlTypes = new()
	{
		[TransportParameterType.String] = "string",
		[TransportParameterType.Number] = "number",
		[TransportParameterType.Boolean] = "boolean",
		[TransportParameterType.Choice] = "choice",
		[TransportParameterType.Password] = "password",
		[TransportParameterType.Secret] = "secret",
		[TransportParameterType.DynamicChoice] = "dynamic-choice",
		[TransportParameterType.Autocomplete] = "autocomplete",
		[TransportParameterType.MultiSelect] = "multiselect",
		[TransportParameterType.Color] = "color",
		[TransportParameterType.File] = "file",
		[TransportParameterType.Folder] = "folder",
		[TransportParameterType.Hotkey] = "hotkey",
		[TransportParameterType.Duration] = "duration",
		[TransportParameterType.DateTime] = "datetime",
		[TransportParameterType.Json] = "json",
		[TransportParameterType.Code] = "code",
		[TransportParameterType.KeyValue] = "keyvalue",
		[TransportParameterType.Object] = "object",
		[TransportParameterType.Array] = "array",
		[TransportParameterType.IpAddress] = "ipaddress",
		[TransportParameterType.Url] = "url",
		[TransportParameterType.Icon] = "icon",
		[TransportParameterType.Image] = "image",
		[TransportParameterType.KeyboardSequence] = "keyboard-sequence",
		[TransportParameterType.KeyboardCombo] = "keyboard-combo",
		[TransportParameterType.WidgetTarget] = "widget-target"
	};

	[Test]
	public void Build_WritesTheClientControlTypeForEveryParameterType()
	{
		var missing = Enum.GetValues<TransportParameterType>()
			.Where(type => !_clientControlTypes.ContainsKey(type))
			.ToList();
		Assert.That(missing, Is.Empty, "a new parameter type needs a control name on both sides");

		var parameters = Enum.GetValues<SdkParameterType>()
			.Select(type => new ActionParameter { Name = type.ToString(), Label = type.ToString(), Type = type })
			.ToList();

		var data = LaunchApplicationButtonJson.Build("integration",
			new ParameterCatalogueAction(parameters),
			new Dictionary<string, string>(),
			"Label",
			iconId: null,
			TestLocalization.Resolver,
			culture: null);

		using var document = JsonDocument.Parse(data);
		using var flows = JsonDocument.Parse(document.RootElement.GetProperty("flows").GetString()!);
		var written = flows.RootElement[0].GetProperty("children")[0].GetProperty("parameters");

		Assert.Multiple(() =>
		{
			foreach (var parameter in written.EnumerateArray())
			{
				var transportType = ActionParameterDefMapper.MapType(
					Enum.Parse<SdkParameterType>(parameter.GetProperty("name").GetString()!));

				Assert.That(parameter.GetProperty("type").GetString(),
					Is.EqualTo(_clientControlTypes[transportType]),
					$"control type for {transportType}");
			}
		});
	}

	private sealed class ParameterCatalogueAction : IActionDefinition
	{
		public ParameterCatalogueAction(IReadOnlyList<ActionParameter> parameters)
		{
			Parameters = parameters;
		}

		public string Id => "catalogue";
		public LocalizedText Name => "Catalogue";
		public LocalizedText Description => "Every parameter type at once";
		public IReadOnlyList<ActionParameter> Parameters { get; }

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}
}
