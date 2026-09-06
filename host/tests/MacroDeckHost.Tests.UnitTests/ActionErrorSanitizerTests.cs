using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security;
using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests;

public class ActionErrorSanitizerTests
{
	[TestCaseSource(nameof(_mappedExceptions))]
	public void Sanitize_maps_each_exception_type_to_its_code(Exception exception, string expectedCode)
	{
		var (code, _) = ActionErrorSanitizer.Sanitize(exception);

		Assert.That(code, Is.EqualTo(expectedCode));
	}

	[Test]
	public void Sanitize_never_echoes_the_raw_exception_message()
	{
		var (_, message) = ActionErrorSanitizer.Sanitize(
			new InvalidOperationException("connection string: user=admin;password=hunter2"));

		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(message), Does.Not.Contain("hunter2"));
			Assert.That(TestLocalization.Resolve(message), Does.Not.Contain("connection string"));
		});
	}

	[Test]
	public void Sanitize_prefers_the_derived_type_over_its_base()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ActionErrorSanitizer.Sanitize(new TaskCanceledException()).Code,
				Is.EqualTo(ActionExecutionErrorCodes.Cancelled));
			Assert.That(ActionErrorSanitizer.Sanitize(new PlatformNotSupportedException()).Code,
				Is.EqualTo(ActionExecutionErrorCodes.Unsupported));
		});
	}

	[Test]
	public void Sanitize_falls_back_to_ACTION_FAILED_for_an_unmapped_exception()
	{
		var (code, _) = ActionErrorSanitizer.Sanitize(new DivideByZeroException());

		Assert.That(code, Is.EqualTo(ActionExecutionErrorCodes.ActionFailed));
	}

	[Test]
	public void ClampMessage_collapses_newlines_and_whitespace_to_single_spaces()
	{
		var clamped = ActionErrorSanitizer.ClampMessage("line one\nline   two\r\nline\tthree");

		Assert.That(clamped, Is.EqualTo("line one line two line three"));
	}

	[Test]
	public void ClampMessage_trims_leading_and_trailing_whitespace()
	{
		var clamped = ActionErrorSanitizer.ClampMessage("   padded message   ");

		Assert.That(clamped, Is.EqualTo("padded message"));
	}

	[Test]
	public void ClampMessage_leaves_a_short_message_untouched()
	{
		var clamped = ActionErrorSanitizer.ClampMessage("short");

		Assert.That(clamped, Is.EqualTo("short"));
	}

	[Test]
	public void ClampMessage_cuts_anything_over_200_characters()
	{
		var clamped = ActionErrorSanitizer.ClampMessage(new string('a', 250));

		Assert.Multiple(() =>
		{
			Assert.That(clamped, Has.Length.EqualTo(200));
			Assert.That(clamped, Is.EqualTo(new string('a', 200)));
		});
	}

	private static readonly object[] _mappedExceptions =
	[
		new object[] { new TaskCanceledException(), ActionExecutionErrorCodes.Cancelled },
		new object[] { new OperationCanceledException(), ActionExecutionErrorCodes.Cancelled },
		new object[] { new TimeoutException(), ActionExecutionErrorCodes.Timeout },
		new object[] { new HttpRequestException(), ActionExecutionErrorCodes.ProviderUnreachable },
		new object[] { new SocketException(), ActionExecutionErrorCodes.ProviderUnreachable },
		new object[] { new WebSocketException(), ActionExecutionErrorCodes.ProviderUnreachable },
		new object[] { new IOException(), ActionExecutionErrorCodes.ProviderUnreachable },
		new object[] { new UnauthorizedAccessException(), ActionExecutionErrorCodes.PermissionDenied },
		new object[] { new SecurityException(), ActionExecutionErrorCodes.PermissionDenied },
		new object[] { new ArgumentException(), ActionExecutionErrorCodes.InvalidParameter },
		new object[] { new FormatException(), ActionExecutionErrorCodes.InvalidParameter },
		new object[] { new JsonException(), ActionExecutionErrorCodes.InvalidParameter },
		new object[] { new PlatformNotSupportedException(), ActionExecutionErrorCodes.Unsupported },
		new object[] { new NotSupportedException(), ActionExecutionErrorCodes.Unsupported },
		new object[] { new InvalidOperationException(), ActionExecutionErrorCodes.ActionFailed }
	];
}
