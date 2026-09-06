using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui.Sessions;
using MacroDeck.Sdk.Actions;
using ActionParameter = MacroDeck.Sdk.Actions.ActionParameter;
using ActionParameterType = MacroDeckHost.Application.Ui.Transport.Messages.Actions.ActionParameterType;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests;

public class GetActionsRequestMessageHandlerTests
{
	private sealed class RichActionDefinition : IActionDefinition
	{
		public string Id => "rich";
		public LocalizedText Name => "Rich";
		public LocalizedText Description => "All the metadata";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
		[
			ActionParameter.Slider("volume",
				min: 0,
				max: 100,
				step: 5,
				label: "Volume",
				description: "Loudness",
				defaultValue: 50),
			ActionParameter.Choice("mode",
				options: [new ActionParameterOption { Value = "a", Label = "Alpha" }],
				label: "Mode",
				defaultValue: "a",
				required: true),
			ActionParameter.Object("advanced",
				children: [ActionParameter.Text("host", label: "Host")]),
			ActionParameter.Array("urls", itemTemplate: ActionParameter.Url("url", autoPrefixHttps: true))
		];

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}

	[Test]
	public async Task Maps_all_parameter_metadata()
	{
		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration { Actions = [new RichActionDefinition()] });
		var handler = new GetActionsRequestMessageHandler(registry, new EmptyRemotePluginSnapshotStore());

		var response = await handler.Handle(new GetActionsRequest(), CancellationToken.None);

		var action = response.Actions.Single();
		Assert.That(action.Parameters, Has.Count.EqualTo(4));

		var volume = action.Parameters[0];
		Assert.Multiple(() =>
		{
			Assert.That(volume.Type, Is.EqualTo(ActionParameterType.Number));
			Assert.That(TestLocalization.Resolve(volume.Label), Is.EqualTo("Volume"));
			Assert.That(TestLocalization.Resolve(volume.Description), Is.EqualTo("Loudness"));
			Assert.That(volume.Min, Is.EqualTo(0));
			Assert.That(volume.Max, Is.EqualTo(100));
			Assert.That(volume.Step, Is.EqualTo(5));
			Assert.That(volume.ShowSlider, Is.True);
			Assert.That(volume.DefaultValue?.GetDouble(), Is.EqualTo(50));
		});

		var mode = action.Parameters[1];
		Assert.Multiple(() =>
		{
			Assert.That(mode.Type, Is.EqualTo(ActionParameterType.Choice));
			Assert.That(mode.Required, Is.True);
			Assert.That(mode.Options, Has.Count.EqualTo(1));
			Assert.That(mode.Options![0].Value, Is.EqualTo("a"));
			Assert.That(TestLocalization.Resolve(mode.Options![0].Label), Is.EqualTo("Alpha"));
		});

		var advanced = action.Parameters[2];
		Assert.Multiple(() =>
		{
			Assert.That(advanced.Type, Is.EqualTo(ActionParameterType.Object));
			Assert.That(advanced.Children, Has.Count.EqualTo(1));
			Assert.That(advanced.Children![0].Name, Is.EqualTo("host"));
		});

		var urls = action.Parameters[3];
		Assert.Multiple(() =>
		{
			Assert.That(urls.Type, Is.EqualTo(ActionParameterType.Array));
			Assert.That(urls.ItemTemplate, Is.Not.Null);
			Assert.That(urls.ItemTemplate!.Type, Is.EqualTo(ActionParameterType.Url));
			Assert.That(urls.ItemTemplate.AutoPrefixHttps, Is.True);
		});
	}

	[Test]
	public async Task Maps_every_sdk_type_to_a_distinct_transport_type()
	{
		var sdkTypes = Enum.GetValues<MacroDeck.Sdk.Actions.ActionParameterType>();
		var parameters = sdkTypes
			.Select(t => new ActionParameter { Name = $"p-{t}", Type = t })
			.ToList();

		var registry = new FakeIntegrationRegistry();
		registry.Add(new FakeIntegration
		{
			Actions = [new CapturingActionDefinition { Parameters = parameters }]
		});
		var handler = new GetActionsRequestMessageHandler(registry, new EmptyRemotePluginSnapshotStore());

		var response = await handler.Handle(new GetActionsRequest(), CancellationToken.None);

		var mapped = response.Actions.Single().Parameters.Select(p => p.Type).ToList();
		Assert.That(mapped, Is.Unique, "every SDK type must map to its own transport type");
	}
}
