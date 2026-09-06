using MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Widgets;

namespace MacroDeckHost.Tests.UnitTests.ConfigFlow;

[TestFixture]
public class ConfigFlowStepMapperTests
{
	[Test]
	public void Map_DropsTheThisWidgetSentinelFromAWidgetTargetField()
	{
		// A config flow belongs to an integration, not a widget, so "$self" has nothing to resolve to.
		// Left in place it would be submitted verbatim as a widget id by anyone who pressed Continue.
		var step = new ConfigFlowStep { StepId = "setup", Fields = [ActionParameter.WidgetTarget("target")] };

		var dto = ConfigFlowStepMapper.Map(step);

		var field = dto.Fields.Single();
		Assert.Multiple(() =>
		{
			Assert.That(field.AllowSelf, Is.False);
			Assert.That(field.DefaultValue, Is.Null);
		});
	}

	[Test]
	public void Map_LeavesTheThisWidgetSentinelAloneOnAnActionParameter()
	{
		// The same parameter used by an action keeps offering it - the config-flow rule must not leak into
		// the shared mapper.
		var def = Application.Ui.Transport.Messages.Actions.ActionParameterDefMapper
			.Map(ActionParameter.WidgetTarget("target"));

		Assert.Multiple(() =>
		{
			Assert.That(def.AllowSelf, Is.True);
			Assert.That(def.DefaultValue?.GetString(), Is.EqualTo(WidgetTargets.Self));
		});
	}

	[Test]
	public void Map_WithoutLinks_ProducesEmptyLinkList()
	{
		var step = new ConfigFlowStep { StepId = "connection", Fields = [ActionParameter.Text("host")] };

		var dto = ConfigFlowStepMapper.Map(step);

		Assert.That(dto.Links, Is.Empty);
	}

	[Test]
	public void Map_WithMultipleLinks_PreservesLabelAndUrlInOrder()
	{
		var step = new ConfigFlowStep
		{
			StepId = "credentials",
			Links =
			[
				new ConfigFlowLink { Label = "Developer portal", Url = "https://example.com/dashboard" },
				new ConfigFlowLink { Label = "Documentation", Url = "https://example.com/docs" }
			],
			Fields = []
		};

		var dto = ConfigFlowStepMapper.Map(step);

		Assert.That(dto.Links, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(dto.Links[0].Label), Is.EqualTo("Developer portal"));
			Assert.That(dto.Links[0].Url, Is.EqualTo("https://example.com/dashboard"));
			Assert.That(TestLocalization.Resolve(dto.Links[1].Label), Is.EqualTo("Documentation"));
			Assert.That(dto.Links[1].Url, Is.EqualTo("https://example.com/docs"));
		});
	}

	[Test]
	public void Map_WithoutInstructionsOrValues_ProducesEmptyLists()
	{
		var step = new ConfigFlowStep { StepId = "connection", Fields = [ActionParameter.Text("host")] };

		var dto = ConfigFlowStepMapper.Map(step);

		Assert.Multiple(() =>
		{
			Assert.That(dto.Instructions, Is.Empty);
			Assert.That(dto.Values, Is.Empty);
		});
	}

	[Test]
	public void Map_WithInstructions_PreservesTextCopyValuesAndOrder()
	{
		var step = new ConfigFlowStep
		{
			StepId = "credentials",
			Instructions =
			[
				new ConfigFlowInstruction { Text = "Open the developer dashboard and create an app." },
				new ConfigFlowInstruction
				{
					Text = "Add this exact Redirect URI:",
					Values =
					[
						new ConfigFlowCopyValue { Label = "Redirect URI", Value = "http://127.0.0.1/callback" },
						new ConfigFlowCopyValue { Label = "Endpoint", Value = "https://example.com/api" }
					]
				}
			],
			Fields = []
		};

		var dto = ConfigFlowStepMapper.Map(step);

		Assert.That(dto.Instructions, Has.Count.EqualTo(2));
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(dto.Instructions[0].Text),
				Is.EqualTo("Open the developer dashboard and create an app."));
			Assert.That(dto.Instructions[0].Values, Is.Empty);

			Assert.That(TestLocalization.Resolve(dto.Instructions[1].Text), Is.EqualTo("Add this exact Redirect URI:"));
			Assert.That(dto.Instructions[1].Values, Has.Count.EqualTo(2));
			Assert.That(TestLocalization.Resolve(dto.Instructions[1].Values[0].Label), Is.EqualTo("Redirect URI"));
			Assert.That(dto.Instructions[1].Values[0].Value, Is.EqualTo("http://127.0.0.1/callback"));
			Assert.That(TestLocalization.Resolve(dto.Instructions[1].Values[1].Label), Is.EqualTo("Endpoint"));
			Assert.That(dto.Instructions[1].Values[1].Value, Is.EqualTo("https://example.com/api"));
		});
	}

	[Test]
	public void Map_WithStepLevelValues_PreservesLabelAndValue()
	{
		var step = new ConfigFlowStep
		{
			StepId = "waiting",
			Values = [new ConfigFlowCopyValue { Label = "Device code", Value = "ABCD-1234" }],
			Fields = []
		};

		var dto = ConfigFlowStepMapper.Map(step);

		Assert.That(dto.Values, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(dto.Values[0].Label), Is.EqualTo("Device code"));
			Assert.That(dto.Values[0].Value, Is.EqualTo("ABCD-1234"));
		});
	}
}
