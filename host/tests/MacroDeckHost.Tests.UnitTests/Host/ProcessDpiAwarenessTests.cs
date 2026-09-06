namespace MacroDeckHost.Tests.UnitTests.Host;

public class ProcessDpiAwarenessTests
{
	[Test]
	public void Configure_is_safe_on_every_platform()
	{
		// Off Windows there is nothing to virtualize, so it must be a silent no-op rather than a
		// PlatformNotSupportedException from the host's very first statement.
		Assert.DoesNotThrow(ProcessDpiAwareness.Configure);
	}

	[Test]
	public void Configure_is_idempotent()
	{
		// Awareness can only be set once per process, so the second call legitimately fails at the OS
		// level. That must not surface: the process already has the awareness we wanted.
		Assert.DoesNotThrow(() =>
		{
			ProcessDpiAwareness.Configure();
			ProcessDpiAwareness.Configure();
		});
	}
}
