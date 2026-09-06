using MacroDeckHost.Integrations.Mouse;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Mouse;

public class MouseActionDefinitionTests
{
	private static readonly string[] _expectedActionIds =
	[
		"click", "move-cursor", "drag", "scroll", "button-down", "button-up", "release-all-buttons"
	];

	private static readonly string[] _expectedButtonValues = ["left", "right", "middle", "back", "forward"];

	private static readonly ActionParameterType[] _allowedTypes =
	[
		ActionParameterType.Choice, ActionParameterType.Number, ActionParameterType.Duration
	];

	private static readonly string[] _buttonOnly = ["button"];

	private static readonly string[] _absoluteAndRelative = ["absolute", "relative"];

	private static readonly string[] _absoluteOnly = ["absolute"];

	[Test]
	public void The_integration_exposes_the_expected_actions()
	{
		var ids = new MouseInputIntegration().Actions.Select(a => a.Id).ToList();

		Assert.That(ids, Is.EquivalentTo(_expectedActionIds));
	}

	[TestCase("click")]
	[TestCase("drag")]
	[TestCase("button-down")]
	[TestCase("button-up")]
	public void Button_bearing_actions_offer_the_same_five_buttons(string actionId)
	{
		var button = Action(actionId).Parameters.Single(p => p.Name == "button");

		Assert.Multiple(() =>
		{
			Assert.That(button.Type, Is.EqualTo(ActionParameterType.Choice));
			Assert.That(button.Options?.Select(o => o.Value), Is.EqualTo(_expectedButtonValues));
		});
	}

	[TestCase("click", "x")]
	[TestCase("click", "y")]
	[TestCase("move-cursor", "x")]
	[TestCase("move-cursor", "y")]
	[TestCase("drag", "fromX")]
	[TestCase("drag", "toY")]
	public void Coordinates_declare_no_minimum(string actionId, string parameterName)
	{
		var coordinate = Action(actionId).Parameters.Single(p => p.Name == parameterName);

		Assert.Multiple(() =>
		{
			Assert.That(coordinate.Type, Is.EqualTo(ActionParameterType.Number));
			Assert.That(coordinate.Min, Is.Null);
		});
	}

	[Test]
	public void Move_cursor_offers_no_stay_where_you_are_option()
	{
		var mode = Action("move-cursor").Parameters.Single(p => p.Name == "positionMode");

		Assert.That(mode.Options?.Select(o => o.Value), Does.Not.Contain("current"));
	}

	[TestCase("click", "x")]
	[TestCase("click", "y")]
	[TestCase("scroll", "x")]
	[TestCase("scroll", "y")]
	[TestCase("button-down", "x")]
	[TestCase("button-down", "y")]
	public void Optional_coordinates_are_shown_only_for_a_coordinate_mode(string actionId, string parameterName)
	{
		var visibility = Action(actionId).Parameters.Single(p => p.Name == parameterName).VisibleWhen;

		Assert.Multiple(() =>
		{
			Assert.That(visibility, Is.Not.Null);
			Assert.That(visibility!.ParameterName, Is.EqualTo("positionMode"));
			Assert.That(visibility.Values, Is.EqualTo(_absoluteAndRelative));
		});
	}

	[TestCase("fromX")]
	[TestCase("fromY")]
	public void Drag_start_coordinates_are_shown_only_for_a_coordinate_start(string parameterName)
	{
		var visibility = Action("drag").Parameters.Single(p => p.Name == parameterName).VisibleWhen;

		Assert.Multiple(() =>
		{
			Assert.That(visibility, Is.Not.Null);
			Assert.That(visibility!.ParameterName, Is.EqualTo("fromMode"));
			Assert.That(visibility.Values, Is.EqualTo(_absoluteOnly));
		});
	}

	[TestCase("drag", "toX")]
	[TestCase("drag", "toY")]
	[TestCase("drag", "fromMode")]
	[TestCase("drag", "toMode")]
	[TestCase("move-cursor", "x")]
	[TestCase("move-cursor", "y")]
	[TestCase("move-cursor", "positionMode")]
	[TestCase("click", "positionMode")]
	[TestCase("scroll", "positionMode")]
	[TestCase("button-down", "positionMode")]
	public void Always_applicable_parameters_carry_no_condition(string actionId, string parameterName)
	{
		var parameter = Action(actionId).Parameters.Single(p => p.Name == parameterName);

		Assert.That(parameter.VisibleWhen, Is.Null);
	}

	[Test]
	public void Every_condition_names_a_parameter_of_the_same_action()
	{
		foreach (var action in new MouseInputIntegration().Actions)
		{
			var names = action.Parameters.Select(p => p.Name).ToList();
			foreach (var parameter in action.Parameters.Where(p => p.VisibleWhen is not null))
			{
				Assert.That(names,
					Does.Contain(parameter.VisibleWhen!.ParameterName),
					$"'{parameter.Name}' of '{action.Id}' depends on a parameter that does not exist");
			}
		}
	}

	[Test]
	public void Every_condition_lists_values_the_mode_actually_offers()
	{
		// A value the choice cannot hold makes the field unreachable instead of conditional.
		foreach (var action in new MouseInputIntegration().Actions)
		{
			foreach (var parameter in action.Parameters.Where(p => p.VisibleWhen is not null))
			{
				var mode = action.Parameters.Single(p => p.Name == parameter.VisibleWhen!.ParameterName);
				var offered = mode.Options?.Select(o => o.Value).ToList();

				Assert.That(offered,
					Is.Not.Null,
					$"'{parameter.Name}' of '{action.Id}' depends on '{mode.Name}', which offers no options");
				Assert.That(parameter.VisibleWhen!.Values,
					Is.SubsetOf(offered!),
					$"'{parameter.Name}' of '{action.Id}' depends on a value '{mode.Name}' never holds");
			}
		}
	}

	[TestCase("click")]
	[TestCase("scroll")]
	[TestCase("button-down")]
	public void Optionally_positioned_actions_default_to_the_current_pointer(string actionId)
	{
		var mode = Action(actionId).Parameters.Single(p => p.Name == "positionMode");

		Assert.Multiple(() =>
		{
			Assert.That(mode.Options?.Select(o => o.Value), Does.Contain("current"));
			Assert.That(mode.DefaultValue, Is.EqualTo("current"));
		});
	}

	[Test]
	public void Release_button_carries_no_position()
	{
		var names = Action("button-up").Parameters.Select(p => p.Name);

		Assert.That(names, Is.EqualTo(_buttonOnly));
	}

	[Test]
	public void Release_all_buttons_takes_no_parameters()
	{
		Assert.That(Action("release-all-buttons").Parameters, Is.Empty);
	}

	[Test]
	public void Every_parameter_uses_a_type_the_client_already_renders()
	{
		var types = new MouseInputIntegration().Actions
			.SelectMany(a => a.Parameters)
			.Select(p => p.Type)
			.Distinct();

		Assert.That(types, Is.SubsetOf(_allowedTypes));
	}

	[Test]
	public void Every_parameter_name_is_unique_within_its_action()
	{
		foreach (var action in new MouseInputIntegration().Actions)
		{
			var names = action.Parameters.Select(p => p.Name).ToList();
			Assert.That(names, Is.Unique, $"Duplicate parameter name in '{action.Id}'");
		}
	}

	private static IActionDefinition Action(string id)
		=> new MouseInputIntegration().Actions.Single(a => a.Id == id);
}
