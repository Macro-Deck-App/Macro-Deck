using System.Runtime.Versioning;
using MacroDeck.Sdk.Input;

namespace MacroDeck.Sdk.Tests.UnitTests.Input;

[TestFixture]
[Platform("MacOsX")]
[SupportedOSPlatform("macos")]
public class NativeKeysMacOsTests
{
	[Test]
	public async Task The_active_layout_names_every_letter_from_a_thread_pool_thread()
	{
		var letters = await Task.Run(() => Enumerable.Range(0, 128)
			.Select(NativeKeys.FromMacOS)
			.Where(key => key is >= KeyCode.A and <= KeyCode.Z)
			.ToHashSet());

		Assert.That(Enumerable.Range(0, 26).Select(offset => KeyCode.A + offset).Where(key => !letters.Contains(key)), Is.Empty);
	}

	[Test]
	public void Keys_that_type_no_character_do_not_depend_on_the_layout()
		=> Assert.Multiple(() =>
		{
			Assert.That(NativeKeys.FromMacOS(36), Is.EqualTo(KeyCode.Enter));
			Assert.That(NativeKeys.FromMacOS(126), Is.EqualTo(KeyCode.ArrowUp));
		});
}
