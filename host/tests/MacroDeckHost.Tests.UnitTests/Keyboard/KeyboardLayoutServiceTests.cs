using MacroDeckHost.Integrations.Keyboard;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Keyboard;

public class KeyboardLayoutServiceTests
{
	private readonly KeyboardLayoutService _layout = new();

	[Test]
	public void Resolves_letter_keys_case_insensitively()
	{
		Assert.That(_layout.TryResolveKey("a", out var lower), Is.True);
		Assert.That(_layout.TryResolveKey("A", out var upper), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(lower, Is.EqualTo(KeyCode.A));
			Assert.That(upper, Is.EqualTo(KeyCode.A));
		});
	}

	[Test]
	public void Resolves_digit_and_named_keys()
	{
		Assert.That(_layout.TryResolveKey("5", out var five), Is.True);
		Assert.That(_layout.TryResolveKey("Enter", out var enter), Is.True);
		Assert.That(_layout.TryResolveKey("F5", out var f5), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(five, Is.EqualTo(KeyCode.D5));
			Assert.That(enter, Is.EqualTo(KeyCode.Enter));
			Assert.That(f5, Is.EqualTo(KeyCode.F5));
		});
	}

	[Test]
	public void Resolves_key_aliases()
	{
		Assert.That(_layout.TryResolveKey("Esc", out var esc), Is.True);
		Assert.That(_layout.TryResolveKey("Return", out var ret), Is.True);
		Assert.That(_layout.TryResolveKey("Up", out var up), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(esc, Is.EqualTo(KeyCode.Escape));
			Assert.That(ret, Is.EqualTo(KeyCode.Enter));
			Assert.That(up, Is.EqualTo(KeyCode.ArrowUp));
		});
	}

	[Test]
	public void Resolves_media_keys_by_canonical_name()
	{
		Assert.That(_layout.TryResolveKey("MediaPlayPause", out var playPause), Is.True);
		Assert.That(_layout.TryResolveKey("MediaTrackNext", out var next), Is.True);
		Assert.That(_layout.TryResolveKey("AudioVolumeUp", out var volumeUp), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(playPause, Is.EqualTo(KeyCode.MediaPlayPause));
			Assert.That(next, Is.EqualTo(KeyCode.MediaTrackNext));
			Assert.That(volumeUp, Is.EqualTo(KeyCode.AudioVolumeUp));
		});
	}

	[Test]
	public void Resolves_media_key_aliases()
	{
		Assert.That(_layout.TryResolveKey("MediaNextTrack", out var next), Is.True);
		Assert.That(_layout.TryResolveKey("VolumeMute", out var mute), Is.True);
		Assert.That(_layout.TryResolveKey("VolumeUp", out var volumeUp), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(next, Is.EqualTo(KeyCode.MediaTrackNext));
			Assert.That(mute, Is.EqualTo(KeyCode.AudioVolumeMute));
			Assert.That(volumeUp, Is.EqualTo(KeyCode.AudioVolumeUp));
		});
	}

	[Test]
	public void Returns_false_for_invalid_key()
	{
		Assert.That(_layout.TryResolveKey("NotAKey", out var key), Is.False);
		Assert.That(_layout.TryResolveKey("", out _), Is.False);
		Assert.That(key, Is.EqualTo(KeyCode.None));
	}

	[Test]
	public void Resolves_modifier_aliases()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_layout.ResolveModifiers(["Ctrl"]), Is.EqualTo(KeyModifier.Control));
			Assert.That(_layout.ResolveModifiers(["Cmd"]), Is.EqualTo(KeyModifier.Meta));
			Assert.That(_layout.ResolveModifiers(["Win"]), Is.EqualTo(KeyModifier.Meta));
			Assert.That(_layout.ResolveModifiers(["Option"]), Is.EqualTo(KeyModifier.Alt));
		});
	}

	[Test]
	public void Combines_multiple_modifiers()
	{
		var result = _layout.ResolveModifiers(["Ctrl", "Shift"]);
		Assert.That(result, Is.EqualTo(KeyModifier.Control | KeyModifier.Shift));
	}

	[Test]
	public void Expands_modifiers_to_left_variants()
	{
		var keys = _layout.ExpandModifiers(KeyModifier.Control | KeyModifier.Alt);
		Assert.That(keys, Is.EquivalentTo([KeyCode.LeftControl, KeyCode.LeftAlt]));
	}
}
