using System.Reflection;
using System.Runtime.InteropServices;
using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.System;

public class WindowsVolumeLayoutTests
{
	private static readonly Type _windowsVolumeService = typeof(IVolumeService).Assembly.GetType(
		"MacroDeckHost.Integrations.System.Volume.WindowsVolumeService",
		throwOnError: true)!;

	[Test]
	public void PropVariant_matches_the_native_size_for_the_process_bitness()
	{
		var propVariant = _windowsVolumeService.GetNestedType("PropVariant", BindingFlags.NonPublic)!;

		Assert.That(Marshal.SizeOf(propVariant), Is.EqualTo(IntPtr.Size == 8 ? 24 : 16));
	}

	[TestCase("IMMDeviceEnumerator", new[] { "EnumAudioEndpoints", "GetDefaultAudioEndpoint", "GetDevice" })]
	[TestCase("IMMDeviceCollection", new[] { "GetCount", "Item" })]
	[TestCase("IMMDevice", new[] { "Activate", "OpenPropertyStore", "GetId", "GetState" })]
	[TestCase("IPropertyStore", new[] { "GetCount", "GetAt", "GetValue", "SetValue", "Commit" })]
	public void Com_interface_methods_are_declared_in_windows_sdk_vtable_order(string name, string[] vtable)
	{
		var type = _windowsVolumeService.GetNestedType(name, BindingFlags.NonPublic)!;

		var declared = type.GetMethods().OrderBy(method => method.MetadataToken).Select(method => method.Name);

		Assert.That(declared, Is.EqualTo(vtable));
	}
}
