using MacroDeckHost.Integrations.System.Metrics;

namespace MacroDeckHost.Tests.UnitTests.System;

public class LinuxDiskParserTests
{
	private const string MountInfo = """
		22 28 0:21 / /proc rw,nosuid,nodev,noexec,relatime shared:12 - proc proc rw
		24 28 0:5 / /dev rw,nosuid,relatime shared:2 - devtmpfs udev rw,size=8000000k
		28 1 259:2 / / rw,relatime shared:1 - ext4 /dev/nvme0n1p2 rw,errors=remount-ro
		30 28 259:1 / /boot/efi rw,relatime shared:3 - vfat /dev/nvme0n1p1 rw
		31 28 7:3 / /snap/core/123 ro,nodev,relatime shared:4 - squashfs /dev/loop3 ro
		32 28 8:17 / /media/user/My\040Stick rw,nosuid,nodev,relatime shared:5 - exfat /dev/sdb1 rw
		33 28 259:2 /var/lib/docker /var/lib/docker rw,relatime shared:1 - ext4 /dev/nvme0n1p2 rw
		34 28 0:45 / /var/lib/docker/overlay2/abc/merged rw,relatime - overlay overlay rw
		35 28 0:30 / /tmp rw,nosuid,nodev shared:6 - tmpfs tmpfs rw
		""";

	[Test]
	public void Only_real_disks_are_listed_once_each_with_spaces_in_paths_decoded()
	{
		var mounts = LinuxDiskParser.ParseMountInfo(MountInfo);

		Assert.That(mounts.Select(m => m.MountPoint), Is.EquivalentTo(new[] { "/", "/media/user/My Stick" }));
	}

	[Test]
	public void A_mount_carries_its_file_system_source_and_device_number()
	{
		var root = LinuxDiskParser.ParseMountInfo(MountInfo).Single(m => m.MountPoint == "/");

		Assert.Multiple(() =>
		{
			Assert.That(root.FileSystem, Is.EqualTo("ext4"));
			Assert.That(root.Source, Is.EqualTo("/dev/nvme0n1p2"));
			Assert.That(root.Device, Is.EqualTo(new DeviceNumber(259, 2)));
		});
	}

	[Test]
	public void The_datasets_of_one_zfs_pool_are_one_disk()
	{
		var mounts = LinuxDiskParser.ParseMountInfo("""
			40 1 0:50 / / rw shared:1 - zfs rpool/ROOT/ubuntu_abc rw,xattr
			41 40 0:51 / /home/user rw shared:2 - zfs rpool/USERDATA/user_abc rw
			42 40 0:52 / /var/log rw shared:3 - zfs rpool/ROOT/ubuntu_abc/var/log rw
			43 40 0:53 / /var/lib rw shared:4 - zfs rpool/ROOT/ubuntu_abc/var/lib rw
			44 40 0:54 / /boot rw shared:5 - zfs bpool/BOOT/ubuntu_abc rw
			45 40 0:55 / /tank rw shared:6 - zfs tank rw
			""");

		Assert.That(mounts.Select(m => m.MountPoint), Is.EquivalentTo(new[] { "/", "/tank" }));
	}

	[Test]
	public void Btrfs_subvolumes_on_one_device_are_one_disk_keeping_the_device_source()
	{
		var mounts = LinuxDiskParser.ParseMountInfo("""
			60 1 0:30 /root / rw,relatime shared:1 - btrfs /dev/mapper/luks-1234 rw,subvol=/root
			61 60 0:30 /home /home rw,relatime shared:2 - btrfs /dev/mapper/luks-1234 rw,subvol=/home
			""");

		var root = mounts.Single();
		Assert.Multiple(() =>
		{
			Assert.That(root.MountPoint, Is.EqualTo("/"));
			Assert.That(root.Device.IsAnonymous, Is.True);
			Assert.That(root.Source, Is.EqualTo("/dev/mapper/luks-1234"));
		});
	}

	[Test]
	public void A_root_on_dev_root_keeps_its_device_number_for_the_statistics()
	{
		var root = LinuxDiskParser.ParseMountInfo("21 1 179:2 / / rw,noatime shared:1 - ext4 /dev/root rw").Single();

		Assert.That(root.Device, Is.EqualTo(new DeviceNumber(179, 2)));
	}

	[Test]
	public void Diskstats_give_bytes_and_busy_times_per_device()
	{
		var stats = LinuxDiskParser.ParseDiskStats("""
			 259       0 nvme0n1 100 0 2000 30 50 0 1000 40 0 70 70 0 0 0 0
			 259       2 nvme0n1p2 80 0 1600 25 40 0 800 35 0 60 60 0 0 0 0
			   7       3 loop3 1 0 2 0 0 0 0 0 0 0 0
			""");

		var partition = stats[new DeviceNumber(259, 2)];
		Assert.Multiple(() =>
		{
			Assert.That(partition.ReadBytes, Is.EqualTo(1600ul * 512));
			Assert.That(partition.WriteBytes, Is.EqualTo(800ul * 512));
			Assert.That(partition.ReadTime, Is.EqualTo(TimeSpan.FromMilliseconds(25)));
			Assert.That(partition.WriteTime, Is.EqualTo(TimeSpan.FromMilliseconds(35)));
			Assert.That(stats, Has.Count.EqualTo(3));
		});
	}

	[Test]
	public void A_device_number_file_is_read_as_major_and_minor()
		=> Assert.That(LinuxDiskParser.ParseDeviceNumber("253:0\n"), Is.EqualTo(new DeviceNumber(253, 0)));

	[Test]
	public void Udev_label_escapes_are_decoded()
		=> Assert.That(LinuxDiskParser.DecodeUdevLabel(@"My\x20Disk"), Is.EqualTo("My Disk"));
}
