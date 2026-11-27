using System.Buffers.Binary;
using System.Text;
using MacroDeckHost.Integrations.Clipboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Clipboard;

[TestFixture]
internal sealed class WindowsDropFilesTests
{
	[Test]
	public void The_file_list_is_a_wide_dropfiles_block_with_a_double_null_terminated_path_list()
	{
		var bytes = WindowsDropFiles.Build([@"C:\Users\me\a.txt", @"D:\Bilder\ö.png"]);

		var header = Enumerable.Range(0, 5)
			.Select(field => BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(field * 4, 4)))
			.ToArray();
		var list = Encoding.Unicode.GetString(bytes, 20, bytes.Length - 20);
		Assert.Multiple(() =>
		{
			Assert.That(header[0], Is.EqualTo(20), "pFiles");
			Assert.That(header[1], Is.Zero, "pt.x");
			Assert.That(header[2], Is.Zero, "pt.y");
			Assert.That(header[3], Is.Zero, "fNC");
			Assert.That(header[4], Is.EqualTo(1), "fWide");
			Assert.That(list, Is.EqualTo("C:\\Users\\me\\a.txt\0D:\\Bilder\\ö.png\0\0"));
		});
	}
}
