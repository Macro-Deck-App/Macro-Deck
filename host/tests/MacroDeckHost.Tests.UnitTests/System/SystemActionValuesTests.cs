using MacroDeckHost.Integrations.System.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class SystemActionValuesTests
{
	[Test]
	public void ReadString_returns_value_or_empty()
	{
		var parameters = new Dictionary<string, object> { ["a"] = "hello" };

		Assert.Multiple(() =>
		{
			Assert.That(SystemActionValues.ReadString(parameters, "a"), Is.EqualTo("hello"));
			Assert.That(SystemActionValues.ReadString(parameters, "missing"), Is.Empty);
		});
	}

	[Test]
	public void ReadInt_coerces_numeric_and_string_forms()
	{
		var parameters = new Dictionary<string, object>
		{
			["i"] = 7,
			["l"] = 9L,
			["d"] = 3.6,
			["s"] = "42",
			["bad"] = "nope"
		};

		Assert.Multiple(() =>
		{
			Assert.That(SystemActionValues.ReadInt(parameters, "i", 0), Is.EqualTo(7));
			Assert.That(SystemActionValues.ReadInt(parameters, "l", 0), Is.EqualTo(9));
			Assert.That(SystemActionValues.ReadInt(parameters, "d", 0), Is.EqualTo(4));
			Assert.That(SystemActionValues.ReadInt(parameters, "s", 0), Is.EqualTo(42));
			Assert.That(SystemActionValues.ReadInt(parameters, "bad", 5), Is.EqualTo(5));
			Assert.That(SystemActionValues.ReadInt(parameters, "missing", 5), Is.EqualTo(5));
		});
	}

	[Test]
	public void ReadDouble_coerces_numeric_and_string_forms()
	{
		var parameters = new Dictionary<string, object>
		{
			["d"] = 2.5,
			["i"] = 3,
			["s"] = "1.25"
		};

		Assert.Multiple(() =>
		{
			Assert.That(SystemActionValues.ReadDouble(parameters, "d", 0), Is.EqualTo(2.5));
			Assert.That(SystemActionValues.ReadDouble(parameters, "i", 0), Is.EqualTo(3));
			Assert.That(SystemActionValues.ReadDouble(parameters, "s", 0), Is.EqualTo(1.25));
			Assert.That(SystemActionValues.ReadDouble(parameters, "missing", 9.5), Is.EqualTo(9.5));
		});
	}

	[Test]
	public void ReadBool_coerces_bool_int_and_string_forms()
	{
		var parameters = new Dictionary<string, object>
		{
			["b"] = true,
			["i"] = 0,
			["s"] = "true"
		};

		Assert.Multiple(() =>
		{
			Assert.That(SystemActionValues.ReadBool(parameters, "b", false), Is.True);
			Assert.That(SystemActionValues.ReadBool(parameters, "i", true), Is.False);
			Assert.That(SystemActionValues.ReadBool(parameters, "s", false), Is.True);
			Assert.That(SystemActionValues.ReadBool(parameters, "missing", true), Is.True);
		});
	}
}
