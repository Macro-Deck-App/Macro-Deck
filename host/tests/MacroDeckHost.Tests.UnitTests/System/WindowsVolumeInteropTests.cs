using System.Reflection;
using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

public class WindowsVolumeInteropTests
{
	// Resolved by name because the type is [SupportedOSPlatform("windows")] and this assembly runs
	// everywhere; only its metadata is inspected, no COM object is activated.
	private static readonly Type _windowsVolumeService = typeof(IVolumeService).Assembly.GetType(
		"MacroDeckHost.Integrations.System.Volume.WindowsVolumeService",
		throwOnError: true)!;

	[Test]
	public void Every_com_method_hands_its_hresult_back_to_the_caller()
	{
		var comInterfaces = _windowsVolumeService
			.GetNestedTypes(BindingFlags.NonPublic)
			.Where(type => type.IsInterface)
			.ToArray();

		Assert.That(comInterfaces, Is.Not.Empty);

		var throwingMethods = comInterfaces
			.SelectMany(type => type.GetMethods())
			.Where(method => !method.MethodImplementationFlags.HasFlag(MethodImplAttributes.PreserveSig))
			.Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
			.ToArray();

		Assert.That(throwingMethods,
			Is.Empty,
			"Without [PreserveSig] the runtime throws a COMException before the caller can inspect the " +
			"HRESULT, so a missing default audio endpoint (0x80070490) cannot be handled gracefully.");
	}
}
