using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using SdkActionParameter = MacroDeck.Sdk.Actions.ActionParameter;

namespace MacroDeckHost.Tests.UnitTests;

[TestFixture]
internal sealed class ActionParameterDefMapperTests
{
	private static readonly string[] _basicAndBearer = ["basic", "bearer"];

	[Test]
	public void A_parameter_without_a_condition_maps_to_no_visibility()
	{
		var mapped = ActionParameterDefMapper.Map(SdkActionParameter.Text("field"));

		Assert.That(mapped.VisibleWhen, Is.Null);
	}

	[Test]
	public void A_visibility_condition_survives_the_mapping()
	{
		var mapped = ActionParameterDefMapper.Map(SdkActionParameter.Secret("authSecret")
			.OnlyWhen("authType", "basic", "bearer"));

		Assert.Multiple(() =>
		{
			Assert.That(mapped.VisibleWhen, Is.Not.Null);
			Assert.That(mapped.VisibleWhen!.ParameterName, Is.EqualTo("authType"));
			Assert.That(mapped.VisibleWhen.Values, Is.EqualTo(_basicAndBearer));
		});
	}

	[Test]
	public void A_child_parameters_condition_survives_the_mapping()
	{
		var mapped = ActionParameterDefMapper.Map(SdkActionParameter.Object("group",
			children: [SdkActionParameter.Text("inner").OnlyWhen("mode", "advanced")]));

		Assert.That(mapped.Children![0].VisibleWhen!.ParameterName, Is.EqualTo("mode"));
	}
}
