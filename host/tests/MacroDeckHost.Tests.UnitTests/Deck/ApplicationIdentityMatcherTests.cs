using MacroDeckHost.Application.Deck;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
public class ApplicationIdentityMatcherTests
{
	[Test]
	public void Normalize_ExecutablePath_ResolvesToFullPathAndTrimsTrailingSeparator()
	{
		var relative = Path.Combine("some", "relative", "app.exe");

		var normalized = ApplicationIdentityMatcher.Normalize(ApplicationIdentityKind.ExecutablePath,
			relative + Path.DirectorySeparatorChar);

		Assert.That(normalized, Is.EqualTo(Path.GetFullPath(relative)));
	}

	[Test]
	public void Normalize_ExecutablePath_TrimsSurroundingWhitespace()
	{
		var normalized = ApplicationIdentityMatcher.Normalize(ApplicationIdentityKind.ExecutablePath, "  app.exe  ");

		Assert.That(normalized, Is.EqualTo(Path.GetFullPath("app.exe")));
	}

	[Test]
	public void Normalize_ExecutablePath_InvalidPath_FallsBackToTrimmedRawValue()
	{
		// Embedded NUL is invalid on every platform GetFullPath supports and throws ArgumentException.
		var invalid = "app\0.exe";

		var normalized = ApplicationIdentityMatcher.Normalize(ApplicationIdentityKind.ExecutablePath, invalid);

		Assert.That(normalized, Is.EqualTo(invalid));
	}

	[TestCase(ApplicationIdentityKind.ProcessName)]
	[TestCase(ApplicationIdentityKind.BundleId)]
	public void Normalize_NonPathKinds_OnlyTrims(ApplicationIdentityKind kind)
	{
		var normalized = ApplicationIdentityMatcher.Normalize(kind, "  com.example.App  ");

		Assert.That(normalized, Is.EqualTo("com.example.App"));
	}

	[Test]
	public void Matches_ExecutablePath_IsCaseInsensitive()
	{
		var rule = Rule(ApplicationIdentityKind.ExecutablePath, "C:\\Apps\\app.exe");
		var app = new FocusedApplication(1, "c:\\apps\\APP.EXE", null, null);

		Assert.That(ApplicationIdentityMatcher.Matches(rule, app), Is.True);
	}

	[Test]
	public void Matches_ProcessName_ComparesTheProcessNameField()
	{
		var rule = Rule(ApplicationIdentityKind.ProcessName, "notepad");
		var app = new FocusedApplication(1, "C:\\Windows\\notepad.exe", "NOTEPAD", null);

		Assert.That(ApplicationIdentityMatcher.Matches(rule, app), Is.True);
	}

	[Test]
	public void Matches_BundleId_ComparesTheBundleIdField()
	{
		var rule = Rule(ApplicationIdentityKind.BundleId, "com.apple.Safari");
		var app = new FocusedApplication(1, null, null, "com.apple.safari");

		Assert.That(ApplicationIdentityMatcher.Matches(rule, app), Is.True);
	}

	[Test]
	public void Matches_NullCorrespondingField_ReturnsFalse()
	{
		var rule = Rule(ApplicationIdentityKind.BundleId, "com.apple.Safari");
		var app = new FocusedApplication(1, "C:\\Safari.exe", "Safari", null);

		Assert.That(ApplicationIdentityMatcher.Matches(rule, app), Is.False);
	}

	[Test]
	public void Matches_DisabledRule_NeverMatches()
	{
		var rule = Rule(ApplicationIdentityKind.ProcessName, "notepad");
		rule.Enabled = false;
		var app = new FocusedApplication(1, null, "notepad", null);

		Assert.That(ApplicationIdentityMatcher.Matches(rule, app), Is.False);
	}

	[Test]
	public void SameTarget_SameDeviceKindAndIdentity_IgnoringCase_IsTrue()
	{
		var deviceId = Guid.NewGuid();
		var a = Rule(ApplicationIdentityKind.ProcessName, "notepad", deviceId);
		var b = Rule(ApplicationIdentityKind.ProcessName, "NOTEPAD", deviceId);

		Assert.That(ApplicationIdentityMatcher.SameTarget(a, b), Is.True);
	}

	[Test]
	public void SameTarget_DifferentDevice_IsFalse()
	{
		var a = Rule(ApplicationIdentityKind.ProcessName, "notepad", Guid.NewGuid());
		var b = Rule(ApplicationIdentityKind.ProcessName, "notepad", Guid.NewGuid());

		Assert.That(ApplicationIdentityMatcher.SameTarget(a, b), Is.False);
	}

	[Test]
	public void SameTarget_DifferentKind_IsFalse()
	{
		var deviceId = Guid.NewGuid();
		var a = Rule(ApplicationIdentityKind.ProcessName, "notepad", deviceId);
		var b = Rule(ApplicationIdentityKind.ExecutablePath, "notepad", deviceId);

		Assert.That(ApplicationIdentityMatcher.SameTarget(a, b), Is.False);
	}

	[Test]
	public void SameTarget_DifferentIdentity_IsFalse()
	{
		var deviceId = Guid.NewGuid();
		var a = Rule(ApplicationIdentityKind.ProcessName, "notepad", deviceId);
		var b = Rule(ApplicationIdentityKind.ProcessName, "wordpad", deviceId);

		Assert.That(ApplicationIdentityMatcher.SameTarget(a, b), Is.False);
	}

	private static FolderFocusRule Rule(ApplicationIdentityKind kind, string identity, Guid? deviceId = null)
		=> new()
		{
			Id = Guid.NewGuid(),
			ApplicationIdentity = identity,
			IdentityKind = kind,
			DeviceId = deviceId ?? Guid.NewGuid()
		};
}
