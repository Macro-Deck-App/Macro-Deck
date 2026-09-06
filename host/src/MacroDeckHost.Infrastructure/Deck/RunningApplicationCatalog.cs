using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Infrastructure.Deck;

public sealed class RunningApplicationCatalog : IRunningApplicationCatalog
{
	public Task<IReadOnlyList<RunningApplication>> GetAsync(string? filter, CancellationToken cancellationToken)
	{
		IEnumerable<RunningApplication> apps = OperatingSystem.IsMacOS() ? CollectMacOs() : CollectByProcess();

		if (!string.IsNullOrWhiteSpace(filter))
		{
			apps = apps.Where(a =>
				a.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
				a.Identity.Contains(filter, StringComparison.OrdinalIgnoreCase));
		}

		IReadOnlyList<RunningApplication> result = apps
			.DistinctBy(a => a.Identity, StringComparer.OrdinalIgnoreCase)
			.OrderBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
			.ToList();

		return Task.FromResult(result);
	}

	private static List<RunningApplication> CollectByProcess()
	{
		var result = new List<RunningApplication>();
		foreach (var process in Process.GetProcesses())
		{
			try
			{
				var name = SafeProcessName(process);
				if (string.IsNullOrEmpty(name))
				{
					continue;
				}

				var path = SafeExecutablePath(process);
				result.Add(path is null
					? new RunningApplication(name, ApplicationIdentityKind.ProcessName, name)
					: new RunningApplication(path, ApplicationIdentityKind.ExecutablePath, name));
			}
			finally
			{
				process.Dispose();
			}
		}

		return result;
	}

	private static string? SafeProcessName(Process process)
	{
		try
		{
			return process.ProcessName;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
	}

	private static string? SafeExecutablePath(Process process)
	{
		try
		{
			return process.MainModule?.FileName;
		}
		catch (Win32Exception)
		{
			return null;
		}
		catch (InvalidOperationException)
		{
			return null;
		}
		catch (NotSupportedException)
		{
			return null;
		}
	}

	[SupportedOSPlatform("macos")]
	private static List<RunningApplication> CollectMacOs()
	{
		try
		{
			if (!NativeLibrary.TryLoad(AppKit, out _))
			{
				return [];
			}

			var workspaceClass = objc_getClass(Utf8("NSWorkspace"));
			if (workspaceClass == IntPtr.Zero)
			{
				return [];
			}

			var sharedWorkspace = objc_msgSend_noArgs(workspaceClass, sel_registerName(Utf8("sharedWorkspace")));
			if (sharedWorkspace == IntPtr.Zero)
			{
				return [];
			}

			var apps = objc_msgSend_noArgs(sharedWorkspace, sel_registerName(Utf8("runningApplications")));
			if (apps == IntPtr.Zero)
			{
				return [];
			}

			var count = CFArrayGetCount(apps);
			var result = new List<RunningApplication>((int)count);
			for (nint i = 0; i < count; i++)
			{
				var app = CFArrayGetValueAtIndex(apps, i);
				if (app == IntPtr.Zero)
				{
					continue;
				}

				var policy = objc_msgSend_intRet(app, sel_registerName(Utf8("activationPolicy")));
				if (policy != 0)
				{
					continue;
				}

				var bundleId = ReadNSString(app, "bundleIdentifier");
				if (string.IsNullOrEmpty(bundleId))
				{
					continue;
				}

				var label = ReadNSString(app, "localizedName") ?? bundleId;
				result.Add(new RunningApplication(bundleId, ApplicationIdentityKind.BundleId, label));
			}

			return result;
		}
		catch
		{
			return [];
		}
	}

	private static string? ReadNSString(IntPtr receiver, string selectorName)
	{
		var value = objc_msgSend_noArgs(receiver, sel_registerName(Utf8(selectorName)));
		if (value == IntPtr.Zero)
		{
			return null;
		}

		var utf8 = objc_msgSend_noArgs(value, sel_registerName(Utf8("UTF8String")));
		return utf8 == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(utf8);
	}

	private static byte[] Utf8(string value) => System.Text.Encoding.UTF8.GetBytes(value + '\0');

	private const string LibObjC = "/usr/lib/libobjc.dylib";
	private const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
	private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	[DllImport(LibObjC)]
	private static extern IntPtr objc_getClass(byte[] name);

	[DllImport(LibObjC)]
	private static extern IntPtr sel_registerName(byte[] name);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend_noArgs(IntPtr receiver, IntPtr selector);

	[DllImport(LibObjC, EntryPoint = "objc_msgSend")]
	private static extern nint objc_msgSend_intRet(IntPtr receiver, IntPtr selector);

	[DllImport(CoreFoundation)]
	private static extern nint CFArrayGetCount(IntPtr array);

	[DllImport(CoreFoundation)]
	private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, nint index);
}
