using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Actions;

namespace MacroDeckHost.Tests.UnitTests.Mouse;

public class MouseActionValuesTests
{
	[TestCase("left", MouseButton.Left)]
	[TestCase("right", MouseButton.Right)]
	[TestCase("middle", MouseButton.Middle)]
	[TestCase("back", MouseButton.Back)]
	[TestCase("forward", MouseButton.Forward)]
	[TestCase("  RIGHT  ", MouseButton.Right)]
	[TestCase("", MouseButton.Left)]
	[TestCase("nonsense", MouseButton.Left)]
	public void ReadButton_parses_and_falls_back_to_left(string value, MouseButton expected)
	{
		var parameters = new Dictionary<string, object> { ["button"] = value };

		Assert.That(MouseActionValues.ReadButton(parameters), Is.EqualTo(expected));
	}

	[Test]
	public void ReadButton_falls_back_when_the_parameter_is_missing()
	{
		Assert.That(MouseActionValues.ReadButton(new Dictionary<string, object>()), Is.EqualTo(MouseButton.Left));
	}

	[TestCase("up", ScrollAxis.Vertical, 1)]
	[TestCase("down", ScrollAxis.Vertical, -1)]
	[TestCase("left", ScrollAxis.Horizontal, -1)]
	[TestCase("right", ScrollAxis.Horizontal, 1)]
	[TestCase("", ScrollAxis.Vertical, -1)]
	public void ReadScrollDirection_maps_to_an_axis_and_a_sign(string value, ScrollAxis axis, int sign)
	{
		var parameters = new Dictionary<string, object> { ["direction"] = value };

		Assert.That(MouseActionValues.ReadScrollDirection(parameters), Is.EqualTo((axis, sign)));
	}

	[TestCase(42, 42)]
	[TestCase(42L, 42)]
	[TestCase(42.0, 42)]
	[TestCase(41.6, 42)]
	[TestCase("42", 42)]
	[TestCase("41.6", 42)]
	[TestCase("-42", -42)]
	[TestCase("not a number", 7)]
	public void ReadInt_accepts_every_type_the_flow_executor_produces(object value, int expected)
	{
		var parameters = new Dictionary<string, object> { ["n"] = value };

		Assert.That(MouseActionValues.ReadInt(parameters, "n", 7), Is.EqualTo(expected));
	}

	[Test]
	public void ReadInt_falls_back_for_a_missing_parameter()
	{
		Assert.That(MouseActionValues.ReadInt(new Dictionary<string, object>(), "n", 7), Is.EqualTo(7));
	}

	[Test]
	public void ReadTarget_reads_an_absolute_position()
	{
		var parameters = new Dictionary<string, object>
		{
			["positionMode"] = "absolute",
			["x"] = -40,
			["y"] = 90
		};

		Assert.That(MouseActionValues.ReadTarget(parameters), Is.EqualTo(MouseTarget.At(-40, 90)));
	}

	[Test]
	public void ReadTarget_reads_a_relative_offset()
	{
		var parameters = new Dictionary<string, object>
		{
			["positionMode"] = "relative",
			["x"] = 5,
			["y"] = -5
		};

		Assert.That(MouseActionValues.ReadTarget(parameters), Is.EqualTo(MouseTarget.By(5, -5)));
	}

	[Test]
	public void ReadTarget_ignores_the_coordinates_at_the_current_position()
	{
		var parameters = new Dictionary<string, object>
		{
			["positionMode"] = "current",
			["x"] = 900,
			["y"] = 900
		};

		Assert.That(MouseActionValues.ReadTarget(parameters), Is.EqualTo(MouseTarget.Current));
	}

	[Test]
	public void ReadTarget_uses_the_given_fallback_for_an_unknown_mode()
	{
		var parameters = new Dictionary<string, object> { ["positionMode"] = "sideways", ["x"] = 1, ["y"] = 2 };

		Assert.Multiple(() =>
		{
			Assert.That(MouseActionValues.ReadTarget(parameters), Is.EqualTo(MouseTarget.Current));
			Assert.That(MouseActionValues.ReadTarget(parameters, fallbackMode: MouseCoordinateMode.Absolute),
				Is.EqualTo(MouseTarget.At(1, 2)));
		});
	}

	[Test]
	public void ReadString_tolerates_a_null_value()
	{
		var parameters = new Dictionary<string, object> { ["button"] = null! };

		Assert.That(MouseActionValues.ReadString(parameters, "button"), Is.Empty);
	}
}
