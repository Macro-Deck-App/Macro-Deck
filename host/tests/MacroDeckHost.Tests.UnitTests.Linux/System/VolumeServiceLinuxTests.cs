using System.Globalization;
using System.Runtime.Versioning;
using MacroDeckHost.Integrations.System.Volume;

namespace MacroDeckHost.Tests.UnitTests.Linux.System;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class VolumeServiceLinuxTests
{
	private static readonly TimeSpan _settle = TimeSpan.FromSeconds(2);
	private static readonly TimeSpan _shortLifetime = TimeSpan.FromSeconds(1.3);

	[Test]
	public async Task A_sink_that_does_not_exist_reads_as_unavailable_rather_than_unmuted()
	{
		var service = VolumeServiceFactory.Create();
		var target = new AudioTarget(AudioFlow.Output, "macro_deck_test.no_such_sink");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(await service.GetMuteAsync(target), Is.Null);
			Assert.That(await service.GetVolumeAsync(target), Is.Null);
			Assert.That(await service.SetMuteAsync(target, true), Is.False);
		});
	}

	[Test]
	public void Listing_devices_does_not_throw_without_an_audio_server()
	{
		var service = VolumeServiceFactory.Create();

		Assert.DoesNotThrowAsync(() => service.GetDevicesAsync());
	}

	[Test]
	public async Task While_subscribed_an_unchanged_volume_is_read_without_running_pactl_again()
	{
		using var pactl = new FakePactl();
		using var service = pactl.CreateService();
		service.Changed += Ignore;
		await Task.Delay(_settle);

		await service.GetVolumeAsync(AudioTarget.DefaultOutput);
		var listings = pactl.Calls("list");
		await Task.Delay(TimeSpan.FromSeconds(1.5));
		var volume = await service.GetVolumeAsync(AudioTarget.DefaultOutput);
		await service.GetMuteAsync(AudioTarget.DefaultOutput);

		Assert.Multiple(() =>
		{
			Assert.That(volume, Is.EqualTo(0.4f));
			Assert.That(pactl.Calls("list"), Is.EqualTo(listings));
		});
	}

	[Test]
	public async Task While_subscribed_an_audio_event_makes_the_next_read_see_the_new_volume()
	{
		using var pactl = new FakePactl();
		using var service = pactl.CreateService();
		service.Changed += Ignore;
		await Task.Delay(_settle);
		await service.GetVolumeAsync(AudioTarget.DefaultOutput);

		pactl.SetVolume(70);
		pactl.RaiseEvent();

		await Eventually(async () => await service.GetVolumeAsync(AudioTarget.DefaultOutput) == 0.7f);
	}

	[Test]
	public async Task Once_the_subscription_ends_a_changed_volume_shows_within_about_a_second()
	{
		using var pactl = new FakePactl();
		using var service = pactl.CreateService();
		service.Changed += Ignore;
		await Task.Delay(_settle);
		await service.GetVolumeAsync(AudioTarget.DefaultOutput);

		pactl.EndSubscription();
		pactl.SetVolume(70);
		await Task.Delay(_shortLifetime);

		Assert.That(await service.GetVolumeAsync(AudioTarget.DefaultOutput), Is.EqualTo(0.7f));
	}

	[Test]
	public async Task Without_a_subscriber_a_changed_volume_shows_within_about_a_second()
	{
		using var pactl = new FakePactl();
		using var service = pactl.CreateService();
		service.Changed += Ignore;
		await Task.Delay(_settle);
		await service.GetVolumeAsync(AudioTarget.DefaultOutput);

		service.Changed -= Ignore;
		pactl.SetVolume(70);
		await Task.Delay(_shortLifetime);

		Assert.That(await service.GetVolumeAsync(AudioTarget.DefaultOutput), Is.EqualTo(0.7f));
	}

	private static void Ignore()
	{
	}

	private static async Task Eventually(Func<Task<bool>> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!await condition())
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "The condition never became true");
			await Task.Delay(50);
		}
	}

	private sealed class FakePactl : IDisposable
	{
		private const string Script = """
									  #!/bin/sh
									  dir=$(dirname "$0")
									  echo "$1" >> "$dir/calls"
									  case "$1" in
									    list) printf 'Sink #1\n\tName: fake_sink\n\tDescription: Fake\n\tMute: no\n\tVolume: mono: 1 / %s%% / 0 dB\n' "$(cat "$dir/volume")" ;;
									    info) printf 'Default Sink: fake_sink\nDefault Source: fake_source\n' ;;
									    subscribe)
									      while [ ! -f "$dir/stop" ]; do
									        if [ -f "$dir/event" ]; then rm -f "$dir/event"; echo "Event 'change' on sink #1"; fi
									        sleep 0.05
									      done ;;
									  esac
									  """;

		private readonly string _directory =
			Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), $"md-pactl-{Guid.NewGuid():N}")).FullName;

		public FakePactl()
		{
			var path = Path.Combine(_directory, "pactl");
			File.WriteAllText(path, Script.ReplaceLineEndings("\n") + "\n");
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			SetVolume(40);
		}

		public LinuxVolumeService CreateService()
		{
			return new LinuxVolumeService(Path.Combine(_directory, "pactl"),
				Path.Combine(_directory, "no-amixer"),
				TimeSpan.FromMinutes(1));
		}

		public void SetVolume(int percent)
		{
			File.WriteAllText(Path.Combine(_directory, "volume"), percent.ToString(CultureInfo.InvariantCulture));
		}

		public void RaiseEvent()
		{
			File.WriteAllText(Path.Combine(_directory, "event"), string.Empty);
		}

		public void EndSubscription()
		{
			File.WriteAllText(Path.Combine(_directory, "stop"), string.Empty);
		}

		public int Calls(string verb)
		{
			var calls = Path.Combine(_directory, "calls");
			return File.Exists(calls) ? File.ReadLines(calls).Count(line => line == verb) : 0;
		}

		public void Dispose()
		{
			EndSubscription();
			Directory.Delete(_directory, true);
		}
	}
}
