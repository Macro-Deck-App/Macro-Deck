using System.Text.Json;
using MacroDeckHost.Integrations.Keyboard.Models;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardSequenceSerializationTests
{
	private static readonly string[] _expectedModifiers = ["Ctrl"];

	private static readonly JsonSerializerOptions _options = new()
	{
		PropertyNameCaseInsensitive = true,
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase
	};

	[Test]
	public void Deserializes_the_documented_example()
	{
		const string json = """
							{
							  "steps": [
							    { "type": "keyCombo", "modifiers": ["Ctrl"], "key": "C" },
							    { "type": "delay", "milliseconds": 100 },
							    { "type": "text", "text": "Hello World" }
							  ]
							}
							""";

		var sequence = JsonSerializer.Deserialize<KeyboardSequence>(json, _options);

		Assert.That(sequence, Is.Not.Null);
		Assert.That(sequence!.Steps, Has.Count.EqualTo(3));

		Assert.That(sequence.Steps[0], Is.TypeOf<KeyComboStep>());
		var combo = (KeyComboStep)sequence.Steps[0];
		Assert.Multiple(() =>
		{
			Assert.That(combo.Modifiers, Is.EqualTo(_expectedModifiers));
			Assert.That(combo.Key, Is.EqualTo("C"));
		});

		Assert.That(sequence.Steps[1], Is.TypeOf<DelayStep>());
		Assert.That(((DelayStep)sequence.Steps[1]).Milliseconds, Is.EqualTo(100));

		Assert.That(sequence.Steps[2], Is.TypeOf<TextStep>());
		Assert.That(((TextStep)sequence.Steps[2]).Text, Is.EqualTo("Hello World"));
	}

	[Test]
	public void Round_trips_all_step_types()
	{
		var original = new KeyboardSequence
		{
			Repeat = 2,
			RepeatDelayMs = 50,
			Steps =
			[
				new KeyComboStep { Modifiers = ["Ctrl", "Shift"], Key = "P", Repeat = 2, RepeatDelayMs = 10 },
				new TextStep { Text = "こんにちは" },
				new DelayStep { Milliseconds = 500 },
				new KeyDownStep { Key = "Shift" },
				new KeyUpStep { Key = "Shift" }
			]
		};

		var json = JsonSerializer.Serialize(original, _options);
		var restored = JsonSerializer.Deserialize<KeyboardSequence>(json, _options);

		Assert.That(restored, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(restored!.Repeat, Is.EqualTo(2));
			Assert.That(restored.RepeatDelayMs, Is.EqualTo(50));
			Assert.That(restored.Steps, Has.Count.EqualTo(5));
			Assert.That(restored.Steps[0], Is.TypeOf<KeyComboStep>());
			Assert.That(restored.Steps[1], Is.TypeOf<TextStep>());
			Assert.That(restored.Steps[2], Is.TypeOf<DelayStep>());
			Assert.That(restored.Steps[3], Is.TypeOf<KeyDownStep>());
			Assert.That(restored.Steps[4], Is.TypeOf<KeyUpStep>());
		});
		Assert.That(((TextStep)restored.Steps[1]).Text, Is.EqualTo("こんにちは"));
	}
}
