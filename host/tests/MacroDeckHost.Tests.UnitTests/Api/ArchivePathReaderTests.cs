using MacroDeckHost.Api.Support;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Api;

public class ArchivePathReaderTests
{
	private static readonly string[] _allowed = [PortableFileExtensions.Profile];

	private string _directory = null!;

	[SetUp]
	public void SetUp()
	{
		_directory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_directory);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("   ")]
	public void A_blank_path_is_a_validation_error(string? path)
	{
		var result = ArchivePathReader.Validate(path, _allowed, 1024);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public void A_relative_path_is_rejected_rather_than_resolved()
	{
		var result = ArchivePathReader.Validate("archive.macroDeckProfile", _allowed, 1024);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public void A_directory_is_not_an_archive()
	{
		var result = ArchivePathReader.Validate(_directory, _allowed, 1024);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public void A_missing_file_is_not_found()
	{
		var path = Path.Combine(_directory, "absent.macroDeckProfile");

		var result = ArchivePathReader.Validate(path, _allowed, 1024);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.NotFound));
		});
	}

	[Test]
	public void An_extension_outside_the_allow_list_is_rejected()
	{
		var path = WriteFile("notes.txt", 4);

		var result = ArchivePathReader.Validate(path, _allowed, 1024);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public void The_extension_match_ignores_case()
	{
		var path = WriteFile("archive.MACRODECKPROFILE", 4);

		Assert.That(ArchivePathReader.Validate(path, _allowed, 1024).Success, Is.True);
	}

	[Test]
	public void A_file_over_the_cap_is_rejected()
	{
		var path = WriteFile("large.macroDeckProfile", 128);

		var result = ArchivePathReader.Validate(path, _allowed, 64);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public async Task A_valid_archive_reads_its_bytes_back()
	{
		var path = WriteFile("archive.macroDeckProfile", 8);

		var result = await ArchivePathReader.Read(path, _allowed, 1024, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data, Has.Length.EqualTo(8));
		});
	}

	[Test]
	public void OpenRead_applies_the_same_validation()
	{
		var path = WriteFile("notes.txt", 4);

		var result = ArchivePathReader.OpenRead(path, _allowed, 1024);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(PortabilityError.ValidationError));
		});
	}

	[Test]
	public void OpenRead_hands_back_a_readable_stream()
	{
		var path = WriteFile("archive.macroDeckProfile", 8);

		var result = ArchivePathReader.OpenRead(path, _allowed, 1024);
		using var stream = result.Data;

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(stream!.Length, Is.EqualTo(8));
		});
	}

	private string WriteFile(string name, int bytes)
	{
		var path = Path.Combine(_directory, name);
		File.WriteAllBytes(path, new byte[bytes]);
		return path;
	}
}
