using MacroDeck.Sdk.Android;

namespace MacroDeck.Sdk.Tests.UnitTests.Android;

[TestFixture]
public class AndroidDeviceTypedOperationsCompatibilityTests
{
	[Test]
	public void An_old_device_refuses_every_typed_operation_as_unsupported()
	{
		IAndroidDevice device = new DeviceWrittenBeforeTypedOperations();

		Assert.Multiple(() =>
		{
			AssertUnsupported(() => device.SendKeyAsync(AndroidKey.Home));
			AssertUnsupported(() => device.TapAsync(1, 2));
			AssertUnsupported(() => device.SwipeAsync(1, 2, 3, 4, TimeSpan.FromMilliseconds(300)));
			AssertUnsupported(() => device.InputTextAsync("hello"));
			AssertUnsupported(() => device.StartAppAsync("com.example.app"));
			AssertUnsupported(() => device.ForceStopAppAsync("com.example.app"));
			AssertUnsupported(() => device.OpenUriAsync("https://example.com"));
			AssertUnsupported(() => device.RebootAsync());
			AssertUnsupported(() => device.CaptureScreenshotAsync());
		});
	}

	[Test]
	public async Task An_old_device_has_no_status_reading_and_never_lacks_usb_permission()
	{
		IAndroidDevice device = new DeviceWrittenBeforeTypedOperations();

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await device.GetStatusAsync(), Is.Null);
			Assert.That(device.LacksUsbPermission, Is.False);
			Assert.That(device.Mode, Is.EqualTo(AndroidDeviceMode.Normal));
			Assert.That(device.State, Is.EqualTo(AndroidDeviceState.Unauthorized));
		});
	}

	[Test]
	public void An_old_manager_has_no_default_device()
	{
		IAndroidDeviceManager manager = new ManagerWrittenBeforeDefaultDevice();

		Assert.Multiple(() =>
		{
			Assert.That(manager.DefaultDevice, Is.Null);
			Assert.That(manager.IsAdbUnavailable, Is.False);
		});
	}

	[Test]
	public void The_unsupported_manager_has_no_default_device()
	{
		Assert.That(((IAndroidDeviceManager)UnsupportedAndroidDeviceManager.Instance).DefaultDevice, Is.Null);
	}

	[Test]
	public void New_enums_keep_their_published_values()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Enum.GetValues<AndroidKey>().Select(key => (int)key), Is.EqualTo(Enumerable.Range(0, 20)));
			Assert.That((int)AndroidKey.Wakeup, Is.EqualTo(13));
			Assert.That((int)AndroidRebootMode.Normal, Is.EqualTo(0));
			Assert.That((int)AndroidRebootMode.Recovery, Is.EqualTo(1));
			Assert.That((int)AndroidRebootMode.Bootloader, Is.EqualTo(2));
			Assert.That(Enum.GetValues<AndroidDeviceMode>().Select(mode => (int)mode),
				Is.EqualTo(Enumerable.Range(0, 5)));
			Assert.That((int)AndroidDeviceMode.Normal, Is.EqualTo(0));
		});
	}

	private static void AssertUnsupported(Func<Task> operation)
	{
		var exception = Assert.ThrowsAsync<AndroidDeviceException>(() => operation());
		Assert.That(exception!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.Unsupported));
	}

	private sealed class DeviceWrittenBeforeTypedOperations : IAndroidDevice
	{
		public string Serial => "R5CT20ABCDE";

		public AndroidDeviceInfo Info => new("Pixel 8", "Google", "shiba");

		public AndroidDeviceState State => AndroidDeviceState.Unauthorized;

		public Task<AndroidShellResult> ExecuteShellAsync(string command, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<AndroidBatteryState> GetBatteryStateAsync(CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task PushFileAsync(string localPath, string remotePath, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task PullFileAsync(string remotePath, string localPath, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task InstallApkAsync(string apkPath, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task UninstallPackageAsync(string packageName, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class ManagerWrittenBeforeDefaultDevice : IAndroidDeviceManager
	{
		public AndroidDeviceAccess Access => AndroidDeviceAccess.Available;

		public IReadOnlyCollection<IAndroidDevice> Devices => [new DeviceWrittenBeforeTypedOperations()];

		public IAndroidDevice? FindDevice(string serial)
		{
			return Devices.FirstOrDefault(device => device.Serial == serial);
		}

		public Task<IAndroidDevice> ConnectAsync(string address, CancellationToken cancellationToken = default)
		{
			throw new NotSupportedException();
		}

		public event EventHandler<AndroidDeviceEventArgs>? DeviceConnected
		{
			add { }
			remove { }
		}

		public event EventHandler<AndroidDeviceEventArgs>? DeviceDisconnected
		{
			add { }
			remove { }
		}

		public event EventHandler<AndroidDeviceEventArgs>? DeviceStateChanged
		{
			add { }
			remove { }
		}

		public event EventHandler? AccessChanged
		{
			add { }
			remove { }
		}
	}
}
