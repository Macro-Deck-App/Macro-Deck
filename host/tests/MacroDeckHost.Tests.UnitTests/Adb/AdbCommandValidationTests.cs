using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbCommandValidationTests
{
	private const string Serial = "R58M12ABCDE";

	[TestCase("")]
	[TestCase("   ")]
	[TestCase("has space")]
	[TestCase("bad$char")]
	public void Blank_or_invalid_serial_is_rejected(string serial)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbTapCommand(serial, 0, 0)));
	}

	[Test]
	public void Oversized_serial_is_rejected()
	{
		var serial = new string('a', 129);

		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbTapCommand(serial, 0, 0)));
	}

	[TestCase("")]
	[TestCase("nodots")]
	[TestCase("1starts.with.digit")]
	[TestCase("com.exa mple.app")]
	[TestCase("com.example.app!")]
	public void Bad_package_names_are_rejected(string package)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbStartAppCommand(Serial, package)));
	}

	[Test]
	public void Oversized_package_name_is_rejected()
	{
		var package = "com." + new string('a', 260);

		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbStartAppCommand(Serial, package)));
	}

	[TestCase("file:///etc/passwd")]
	[TestCase("content://media/external/images/1")]
	[TestCase("relative/path")]
	[TestCase("not a uri")]
	public void Disallowed_or_invalid_uris_are_rejected(string uri)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbOpenUriCommand(Serial, uri)));
	}

	[Test]
	public void Oversized_uri_is_rejected()
	{
		var uri = "https://example.com/" + new string('a', 2000);

		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbOpenUriCommand(Serial, uri)));
	}

	[Test]
	public void Non_ASCII_input_text_is_rejected()
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbInputTextCommand(Serial, "héllo")));
	}

	[Test]
	public void Oversized_input_text_is_rejected()
	{
		var text = new string('a', 501);

		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbInputTextCommand(Serial, text)));
	}

	[TestCase('\t')]
	[TestCase('\n')]
	[TestCase((char)0x7F)]
	public void Non_printable_characters_in_input_text_are_rejected(char character)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbInputTextCommand(Serial, $"a{character}b")));
	}

	[TestCase(-1, 0)]
	[TestCase(0, -1)]
	[TestCase(8193, 0)]
	[TestCase(0, 8193)]
	public void Out_of_range_tap_coordinates_are_rejected(int x, int y)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbTapCommand(Serial, x, y)));
	}

	[TestCase(0, 0)]
	[TestCase(8192, 8192)]
	public void Boundary_tap_coordinates_are_accepted(int x, int y)
	{
		Assert.That(AdbCommandBuilder.Build(new AdbTapCommand(Serial, x, y)).Success, Is.True);
	}

	[TestCase(-1, 0, 0, 0)]
	[TestCase(0, -1, 0, 0)]
	[TestCase(0, 0, 8193, 0)]
	[TestCase(0, 0, 0, 8193)]
	public void Out_of_range_swipe_coordinates_are_rejected(int x1, int y1, int x2, int y2)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbSwipeCommand(Serial, x1, y1, x2, y2, 0)));
	}

	[TestCase(-1)]
	[TestCase(5001)]
	public void Out_of_range_swipe_duration_is_rejected(int durationMs)
	{
		AssertInvalidParameter(AdbCommandBuilder.Build(new AdbSwipeCommand(Serial, 0, 0, 0, 0, durationMs)));
	}

	[TestCase(0)]
	[TestCase(5000)]
	public void Boundary_swipe_duration_is_accepted(int durationMs)
	{
		Assert.That(AdbCommandBuilder.Build(new AdbSwipeCommand(Serial, 0, 0, 0, 0, durationMs)).Success, Is.True);
	}

	[Test]
	public void Malformed_commands_never_throw()
	{
		Assert.Multiple(() =>
		{
			Assert.DoesNotThrow(() => AdbCommandBuilder.Build(new AdbTapCommand("", int.MinValue, int.MaxValue)));
			Assert.DoesNotThrow(() => AdbCommandBuilder.Build(new AdbStartAppCommand("!!!", "///")));
			Assert.DoesNotThrow(() => AdbCommandBuilder.Build(new AdbOpenUriCommand(Serial, "\0")));
			Assert.DoesNotThrow(() =>
				AdbCommandBuilder.Build(new AdbInputTextCommand(Serial, new string('x', 10_000))));
		});
	}

	private static void AssertInvalidParameter(Result<IReadOnlyList<string>, AdbFailureCode> result)
	{
		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.InvalidParameter));
		});
	}
}
