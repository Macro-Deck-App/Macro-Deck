using System.Text;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class ReverseLineReaderTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.LogsDirectory);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Lines_Come_Back_Newest_First_With_Their_Offsets()
	{
		var path = Write("one\ntwo\nthree\n");

		var lines = ReverseLineReader.Read(path, long.MaxValue).ToList();

		string[] expected = ["three", "two", "one"];
		Assert.Multiple(() =>
		{
			Assert.That(lines.Select(line => line.Text), Is.EqualTo(expected));
			Assert.That(lines[2].Offset, Is.EqualTo(0));
			Assert.That(lines[1].Offset, Is.EqualTo(4));
			Assert.That(lines[0].Offset, Is.EqualTo(8));
		});
	}

	[Test]
	public void An_Empty_File_Yields_Nothing()
	{
		Assert.That(ReverseLineReader.Read(Write(string.Empty), long.MaxValue), Is.Empty);
	}

	[Test]
	public void A_Trailing_Partial_Line_Is_Excluded_By_The_End_Offset()
	{
		var path = Write("one\ntwo\nhalf-writ");

		var end = ReverseLineReader.EndOfLastCompleteLine(path);
		var lines = ReverseLineReader.Read(path, end).ToList();

		string[] expected = ["two", "one"];
		Assert.That(lines.Select(line => line.Text), Is.EqualTo(expected));
	}

	[Test]
	public void Carriage_Returns_Are_Trimmed()
	{
		var lines = ReverseLineReader.Read(Write("one\r\ntwo\r\n"), long.MaxValue).ToList();

		string[] expected = ["two", "one"];
		Assert.That(lines.Select(line => line.Text), Is.EqualTo(expected));
	}

	// The reader walks the file in fixed chunks; a line straddling a boundary must not be split.
	[Test]
	public void A_Line_Spanning_A_Chunk_Boundary_Is_Reassembled()
	{
		var longLine = new string('x', ReverseLineReader.ChunkBytes + 100);
		var path = Write($"first\n{longLine}\nlast\n");

		var lines = ReverseLineReader.Read(path, long.MaxValue).ToList();

		string[] expected = ["last", longLine, "first"];
		Assert.That(lines.Select(line => line.Text), Is.EqualTo(expected));
	}

	[Test]
	public void Multi_Byte_Characters_Survive_A_Chunk_Boundary()
	{
		var padding = new string('a', ReverseLineReader.ChunkBytes - 3);
		var path = Write($"{padding}\nkäse ünd emoji 🎛\nlast\n");

		var lines = ReverseLineReader.Read(path, long.MaxValue).ToList();

		Assert.That(lines[1].Text, Is.EqualTo("käse ünd emoji 🎛"));
	}

	[Test]
	public void A_Byte_Order_Mark_Is_Stripped_From_The_First_Line()
	{
		var path = Path.Combine(_paths.LogsDirectory, "bom.log");
		File.WriteAllText(path, "first\nsecond\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

		var lines = ReverseLineReader.Read(path, long.MaxValue).ToList();

		Assert.That(lines[^1].Text, Is.EqualTo("first"));
	}

	[Test]
	public void The_End_Of_A_File_Without_Any_Newline_Is_Zero()
	{
		Assert.That(ReverseLineReader.EndOfLastCompleteLine(Write("no newline yet")), Is.EqualTo(0));
	}

	private string Write(string content)
	{
		var path = Path.Combine(_paths.LogsDirectory, "sample.log");
		File.WriteAllText(path, content, new UTF8Encoding(false));

		return path;
	}
}
