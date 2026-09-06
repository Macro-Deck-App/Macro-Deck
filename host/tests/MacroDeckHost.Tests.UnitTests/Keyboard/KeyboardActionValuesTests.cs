using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardActionValuesTests
{
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

		Assert.That(KeyboardActionValues.ReadInt(parameters, "n", 7), Is.EqualTo(expected));
	}

	[TestCase("42", 42)]
	[TestCase("41.6", 42)]
	[TestCase("-42", -42)]
	[SetCulture("de-DE")]
	public void ReadInt_parses_a_variable_backed_string_invariant(string value, int expected)
	{
		var parameters = new Dictionary<string, object> { ["n"] = value };

		Assert.That(KeyboardActionValues.ReadInt(parameters, "n", 7), Is.EqualTo(expected));
	}

	[Test]
	public void ReadInt_falls_back_for_a_missing_parameter()
	{
		Assert.That(KeyboardActionValues.ReadInt(new Dictionary<string, object>(), "n", 7), Is.EqualTo(7));
	}

	[Test]
	public void A_focus_failure_is_reported_as_a_refusal_and_not_as_a_missing_application()
	{
		var result = KeyboardActionValues.SessionUnavailableResult(KeyboardSessionUnavailableReason.FocusFailed);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Not.Contain("could not be found"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("foreground"));
		});
	}

	[Test]
	public void ReadString_tolerates_a_null_value()
	{
		var parameters = new Dictionary<string, object> { ["targetProcess"] = null! };

		Assert.That(KeyboardActionValues.ReadString(parameters, "targetProcess"), Is.Empty);
	}
}
