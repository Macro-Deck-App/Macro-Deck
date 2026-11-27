using System.Text;
using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Clipboard;

[TestFixture]
internal sealed class LinuxClipboardCommandsTests
{
	[TestCase("text/plain\ntext/plain;charset=utf-8\nUTF8_STRING\n", ClipboardContentKinds.Text)]
	[TestCase("TARGETS\nTIMESTAMP\nMULTIPLE\nUTF8_STRING\nSTRING\nTEXT\n", ClipboardContentKinds.Text)]
	[TestCase("image/png\n", ClipboardContentKinds.Image)]
	[TestCase("x-special/gnome-copied-files\ntext/uri-list\n", ClipboardContentKinds.Files)]
	[TestCase("text/uri-list\ntext/plain\n", ClipboardContentKinds.Files | ClipboardContentKinds.Text)]
	[TestCase("application/x-color\n", ClipboardContentKinds.Other)]
	[TestCase("TARGETS\nTIMESTAMP\nchromium/x-web-custom-data\n", ClipboardContentKinds.Other)]
	[TestCase("image/jpeg\nchromium/x-source-url\n", ClipboardContentKinds.Image)]
	[TestCase("", ClipboardContentKinds.None)]
	[TestCase("TARGETS\nTIMESTAMP\n", ClipboardContentKinds.None)]
	public void Offered_types_map_to_content_kinds(string listing, ClipboardContentKinds expected)
	{
		Assert.That(LinuxClipboardCommands.ParseKinds(listing), Is.EqualTo(expected));
	}

	[TestCase("Nothing is copied\n", true)]
	[TestCase("Error: target TARGETS not available\n", true)]
	[TestCase("Failed to connect to a Wayland server\n", false)]
	[TestCase("Error: Can't open display: :0\n", false)]
	public void Only_an_empty_clipboard_reads_as_no_content(string error, bool expected)
	{
		Assert.That(LinuxClipboardCommands.MeansNoContent(error), Is.EqualTo(expected));
	}

	[TestCase(0, "", false, "", ClipboardContentKinds.None)]
	[TestCase(0, "a", false, "", ClipboardContentKinds.Text)]
	[TestCase(-1, "", true, "", ClipboardContentKinds.Text)]
	[TestCase(1, "", false, "Nothing is copied", ClipboardContentKinds.None)]
	[TestCase(1, "", false, "Can't open display", ClipboardContentKinds.Text)]
	public void A_text_selection_that_yields_no_bytes_counts_as_empty(
		int exitCode,
		string output,
		bool truncated,
		string error,
		ClipboardContentKinds expected)
	{
		var probe = new ClipboardProcessResult(exitCode, Encoding.UTF8.GetBytes(output), truncated, error);

		Assert.That(LinuxClipboardCommands.KindsAfterTextProbe(ClipboardContentKinds.Text, probe),
			Is.EqualTo(expected));
	}

	[Test]
	public void Only_a_selection_of_nothing_but_text_is_probed_for_emptiness()
	{
		var empty = new ClipboardProcessResult(0, [], false, string.Empty);

		Assert.That(LinuxClipboardCommands.KindsAfterTextProbe(ClipboardContentKinds.Text | ClipboardContentKinds.Files,
				empty),
			Is.EqualTo(ClipboardContentKinds.Text | ClipboardContentKinds.Files));
	}

	[TestCase("/home/me/pic.png", "image/png")]
	[TestCase("/home/me/pic.JPG", "image/jpeg")]
	[TestCase("/home/me/pic.jpeg", "image/jpeg")]
	[TestCase("/home/me/pic.gif", "image/gif")]
	[TestCase("/home/me/pic.bmp", "image/bmp")]
	[TestCase("/home/me/pic.tif", "image/tiff")]
	[TestCase("/home/me/pic.tiff", "image/tiff")]
	[TestCase("/home/me/pic.webp", null)]
	[TestCase("/home/me/pic", null)]
	public void An_image_is_offered_under_the_type_its_extension_names(string path, string? expected)
	{
		Assert.That(LinuxClipboardCommands.ImageType(path), Is.EqualTo(expected));
	}

	[Test]
	public void A_file_whose_bytes_are_not_the_named_image_type_is_recognised()
	{
		byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

		Assert.Multiple(() =>
		{
			Assert.That(LinuxClipboardCommands.HasImageSignature("image/png", png), Is.True);
			Assert.That(LinuxClipboardCommands.HasImageSignature("image/jpeg", png), Is.False);
			Assert.That(LinuxClipboardCommands.HasImageSignature("image/png", "not an image"u8), Is.False);
			Assert.That(LinuxClipboardCommands.HasImageSignature("image/gif", "GIF89a"u8), Is.True);
		});
	}

	[Test]
	public void Files_are_offered_as_a_uri_list_with_every_unsafe_character_escaped()
	{
		var list = LinuxClipboardCommands.BuildUriList(["/home/me/My Notes #1.txt", "/tmp/100%/äö?.png", "/srv/a"]);

		Assert.That(list,
			Is.EqualTo("file:///home/me/My%20Notes%20%231.txt\r\n"
				+ "file:///tmp/100%25/%C3%A4%C3%B6%3F.png\r\n"
				+ "file:///srv/a\r\n"));
	}

	[Test]
	public void Wayland_uses_wl_clipboard_and_x11_uses_xclip_on_the_clipboard_selection()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Describe(LinuxClipboardCommands.ListTypes(true)), Is.EqualTo("wl-paste --list-types"));
			Assert.That(Describe(LinuxClipboardCommands.ReadText(true)),
				Is.EqualTo("wl-paste --no-newline --type text"));
			Assert.That(Describe(LinuxClipboardCommands.Copy(true, "image/png")),
				Is.EqualTo("wl-copy --type image/png"));
			Assert.That(Describe(LinuxClipboardCommands.Clear(true)), Is.EqualTo("wl-copy --clear"));
			Assert.That(Describe(LinuxClipboardCommands.Watch()), Is.EqualTo("wl-paste --watch echo"));

			Assert.That(Describe(LinuxClipboardCommands.ListTypes(false)),
				Is.EqualTo("xclip -selection clipboard -t TARGETS -o"));
			Assert.That(Describe(LinuxClipboardCommands.ReadText(false)), Is.EqualTo("xclip -selection clipboard -o"));
			Assert.That(Describe(LinuxClipboardCommands.Copy(false, null)),
				Is.EqualTo("xclip -selection clipboard -i"));
			Assert.That(Describe(LinuxClipboardCommands.Copy(false, LinuxClipboardCommands.UriListType)),
				Is.EqualTo("xclip -selection clipboard -t text/uri-list -i"));
			Assert.That(Describe(LinuxClipboardCommands.Clear(false)), Is.EqualTo("xclip -selection clipboard -i"));
		});
	}

	[Test]
	public void A_missing_tool_names_the_package_that_provides_it()
	{
		var wayland = LinuxClipboardCommands.Probe(true, tool => tool == "wl-paste");
		var x11 = LinuxClipboardCommands.Probe(false, _ => false);
		var present = LinuxClipboardCommands.Probe(false, tool => tool == "xclip");

		Assert.Multiple(() =>
		{
			Assert.That(wayland,
				Is.EqualTo(ClipboardAvailability.Unavailable(ClipboardUnavailableReason.ToolMissing, "wl-clipboard")));
			Assert.That(x11,
				Is.EqualTo(ClipboardAvailability.Unavailable(ClipboardUnavailableReason.ToolMissing, "xclip")));
			Assert.That(present.IsAvailable, Is.True);
		});
	}

	private static string Describe(ClipboardCommand command)
	{
		return string.Join(' ', [command.FileName, .. command.Arguments]);
	}
}
