using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbPlatformToolsInstallerTests
{
	private static readonly (string Entry, string Reason)[] _maliciousEntries =
	[
		("../evil", "single parent traversal, Unix separator"),
		("../../evil", "double parent traversal, Unix separator"),
		(@"..\evil", "single parent traversal, Windows separator"),
		("platform-tools/../../../evil", "traversal starting from a legitimate-looking subdirectory"),
		("/etc/passwd", "absolute path, Unix separator"),
		(@"C:\evil.exe", "absolute path with a Windows drive letter"),
		(@"\evil", "rooted path with no drive letter (Windows semantics)"),
		(@"\\server\share\evil", "UNC path"),
	];

	[Test]
	public void ResolveDownloadUrl_returns_the_windows_archive_url()
	{
		var url = AdbPlatformToolsInstaller.ResolveDownloadUrl(isWindows: true, isMacOs: false, isLinux: false);

		Assert.That(url, Is.EqualTo("https://dl.google.com/android/repository/platform-tools-latest-windows.zip"));
	}

	[Test]
	public void ResolveDownloadUrl_returns_the_macos_archive_url()
	{
		var url = AdbPlatformToolsInstaller.ResolveDownloadUrl(isWindows: false, isMacOs: true, isLinux: false);

		Assert.That(url, Is.EqualTo("https://dl.google.com/android/repository/platform-tools-latest-darwin.zip"));
	}

	[Test]
	public void ResolveDownloadUrl_returns_the_linux_archive_url()
	{
		var url = AdbPlatformToolsInstaller.ResolveDownloadUrl(isWindows: false, isMacOs: false, isLinux: true);

		Assert.That(url, Is.EqualTo("https://dl.google.com/android/repository/platform-tools-latest-linux.zip"));
	}

	[Test]
	public void ResolveDownloadUrl_returns_null_when_no_platform_flag_is_set()
	{
		var url = AdbPlatformToolsInstaller.ResolveDownloadUrl(isWindows: false, isMacOs: false, isLinux: false);

		Assert.That(url, Is.Null);
	}

	[TestCaseSource(nameof(MaliciousEntryNames))]
	public void Zip_slip_entries_are_rejected(string entryName, string reason)
	{
		var targetDirectory = Path.Combine(Path.GetTempPath(), $"adb-platform-tools-test-{Guid.NewGuid():N}");

		var result = AdbPlatformToolsInstaller.ResolveEntryDestination(entryName, targetDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False, $"'{entryName}' ({reason}) must be rejected.");
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.CommandFailed));
			Assert.That(result.ErrorMessage, Is.Not.Null.And.Not.Empty);
			Assert.That(result.Data, Is.Null);
		});
	}

	private static IEnumerable<TestCaseData> MaliciousEntryNames()
		=> _maliciousEntries.Select(entry =>
			new TestCaseData(entry.Entry, entry.Reason).SetName($"Zip_slip_entries_are_rejected({entry.Reason})"));

	[Test]
	public void A_normal_entry_resolves_inside_the_target_and_keeps_its_subdirectory()
	{
		var targetDirectory = Path.Combine(Path.GetTempPath(), $"adb-platform-tools-test-{Guid.NewGuid():N}");
		var expected = Path.GetFullPath(Path.Combine(targetDirectory, "platform-tools", "adb"));

		var result = AdbPlatformToolsInstaller.ResolveEntryDestination("platform-tools/adb", targetDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data, Is.EqualTo(expected));
		});
	}

	[Test]
	public void A_nested_normal_entry_keeps_its_full_subdirectory_chain()
	{
		var targetDirectory = Path.Combine(Path.GetTempPath(), $"adb-platform-tools-test-{Guid.NewGuid():N}");
		var expected = Path.GetFullPath(Path.Combine(targetDirectory, "platform-tools", "systrace", "systrace.py"));

		var result = AdbPlatformToolsInstaller.ResolveEntryDestination("platform-tools/systrace/systrace.py",
			targetDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data, Is.EqualTo(expected));
		});
	}

	// Guards against an overly-blunt guard (for example a plain "contains .." substring check) that would
	// reject a perfectly normal filename just because it happens to contain two dots.
	[Test]
	public void An_entry_name_containing_two_dots_as_part_of_a_filename_is_not_treated_as_traversal()
	{
		var targetDirectory = Path.Combine(Path.GetTempPath(), $"adb-platform-tools-test-{Guid.NewGuid():N}");
		var expected = Path.GetFullPath(Path.Combine(targetDirectory, "platform-tools", "NOTICE..txt"));

		var result = AdbPlatformToolsInstaller.ResolveEntryDestination("platform-tools/NOTICE..txt", targetDirectory);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data, Is.EqualTo(expected));
		});
	}

	[Test]
	public void A_directory_entry_resolves_inside_the_target()
	{
		var targetDirectory = Path.Combine(Path.GetTempPath(), $"adb-platform-tools-test-{Guid.NewGuid():N}");

		var result = AdbPlatformToolsInstaller.ResolveEntryDestination("platform-tools/", targetDirectory);

		Assert.That(result.Success, Is.True);
	}
}
