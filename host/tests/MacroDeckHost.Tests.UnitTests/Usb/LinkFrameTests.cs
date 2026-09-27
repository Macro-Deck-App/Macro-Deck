using MacroDeckHost.Infrastructure.Usb.Native;

namespace MacroDeckHost.Tests.UnitTests.Usb;

public class LinkFrameTests
{
	private static readonly DateTimeOffset _start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

	[Test]
	public void A_frame_is_type_flags_stream_and_length_in_big_endian_followed_by_the_payload()
	{
		var frame = LinkFrame.Data(0x0102, new byte[] { 0xAA, 0xBB, 0xCC }).Encode();

		Assert.That(frame, Is.EqualTo(new byte[] { 2, 0, 0x01, 0x02, 0, 0, 0, 3, 0xAA, 0xBB, 0xCC }));
	}

	[Test]
	public void Hello_carries_the_magic_the_version_the_epoch_and_the_echo()
	{
		var frame = LinkFrame.Hello(ack: true, epoch: 0x11223344, echo: 0x55667788).Encode();

		Assert.That(frame, Is.EqualTo(new byte[]
		{
			0, 1, 0, 0, 0, 0, 0, 13,
			(byte)'M', (byte)'D', (byte)'L', (byte)'K',
			1,
			0x11, 0x22, 0x33, 0x44,
			0x55, 0x66, 0x77, 0x88
		}));
	}

	[Test]
	public void The_hello_example_pinned_by_the_companion_app_encodes_and_parses_byte_for_byte()
	{
		byte[] pinned =
		[
			0x00, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0D, 0x4D, 0x44, 0x4C, 0x4B, 0x01, 0x01, 0x02, 0x03, 0x04,
			0xA0, 0xB0, 0xC0, 0xD0
		];
		var reader = new LinkFrameReader();
		var output = new List<LinkInbound>();

		reader.Append(pinned, _start, output);

		Assert.Multiple(() =>
		{
			Assert.That(LinkFrame.Hello(ack: true, epoch: 0x01020304, echo: 0xA0B0C0D0).Encode(), Is.EqualTo(pinned));
			Assert.That(LinkHello.Parse(output.Single().Frame),
				Is.EqualTo(new LinkHello(Ack: true, MaxVersion: 1, Epoch: 0x01020304, Echo: 0xA0B0C0D0)));
		});
	}

	[Test]
	public void A_longer_hello_from_a_later_version_is_found_while_seeking_and_its_extra_bytes_are_ignored()
	{
		var hello = LinkFrame.Hello(false, 5, 0, maxVersion: 2).Encode();
		var longer = new byte[hello.Length + 3];
		hello.CopyTo(longer, 0);
		longer[7] = 16;
		var reader = new LinkFrameReader();
		var output = new List<LinkInbound>();

		reader.Append(new byte[] { 7, 7 }.Concat(longer).Concat(LinkFrame.Open(2).Encode()).ToArray(), _start, output);

		Assert.Multiple(() =>
		{
			Assert.That(output.Select(item => item.Frame.Type), Is.EqualTo(new[] { LinkFrameType.Hello, LinkFrameType.Open }));
			Assert.That(LinkHello.Parse(output[0].Frame), Is.EqualTo(new LinkHello(false, 2, 5, 0)));
		});
	}

	[Test]
	public void Window_carries_a_four_byte_credit_and_the_remaining_types_carry_nothing()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LinkFrame.Window(7, 262144).Encode(), Is.EqualTo(new byte[] { 4, 0, 0, 7, 0, 0, 0, 4, 0, 4, 0, 0 }));
			Assert.That(LinkFrame.Open(7).Encode(), Is.EqualTo(new byte[] { 1, 0, 0, 7, 0, 0, 0, 0 }));
			Assert.That(LinkFrame.Close(7).Encode(), Is.EqualTo(new byte[] { 3, 0, 0, 7, 0, 0, 0, 0 }));
			Assert.That(LinkFrame.Bye().Encode(), Is.EqualTo(new byte[] { 5, 0, 0, 0, 0, 0, 0, 0 }));
		});
	}

	[Test]
	public void A_payload_above_16376_bytes_cannot_be_encoded()
	{
		Assert.Multiple(() =>
		{
			Assert.That(LinkFrame.Data(1, new byte[16376]).Encode(), Has.Length.EqualTo(16384));
			Assert.That(() => LinkFrame.Data(1, new byte[16377]).Encode(), Throws.TypeOf<ArgumentOutOfRangeException>());
		});
	}

	[TestCase(21, 21)]
	[TestCase(64, 65)]
	[TestCase(128, 129)]
	[TestCase(512, 513)]
	[TestCase(16320, 16321)]
	[TestCase(16384, 16384)]
	[TestCase(100, 100)]
	public void A_usb_transfer_gets_one_filler_byte_only_when_it_would_end_on_a_full_packet(int frameLength,
		int transferLength)
	{
		var transfer = LinkProtocol.ForUsbTransfer(new byte[frameLength]);

		Assert.Multiple(() =>
		{
			Assert.That(transfer, Has.Length.EqualTo(transferLength));
			if (transferLength > frameLength)
			{
				Assert.That(transfer[^1], Is.EqualTo(0xFF));
			}
		});
	}

	[Test]
	public void The_reader_ignores_bytes_until_a_hello_and_then_parses_frames_split_across_reads()
	{
		var reader = new LinkFrameReader();
		var output = new List<LinkInbound>();
		var bytes = new byte[] { 9, 9, 9 }
			.Concat(LinkFrame.Hello(false, 1, 0).Encode())
			.Concat(LinkFrame.Data(3, "abc"u8.ToArray()).Encode())
			.ToArray();

		foreach (var single in bytes)
		{
			reader.Append([single], _start, output);
		}

		Assert.Multiple(() =>
		{
			Assert.That(output.Select(item => item.Frame.Type), Is.EqualTo(new[] { LinkFrameType.Hello, LinkFrameType.Data }));
			Assert.That(output.Any(item => item.Resync), Is.False);
			Assert.That(output[1].Frame.Payload.ToArray(), Is.EqualTo("abc"u8.ToArray()));
		});
	}

	[Test]
	public void The_reader_parses_several_frames_from_one_read_and_skips_filler_between_them()
	{
		var reader = new LinkFrameReader();
		var output = new List<LinkInbound>();
		var bytes = LinkFrame.Hello(false, 1, 0).Encode()
			.Append((byte)0xFF)
			.Concat(LinkFrame.Open(2).Encode())
			.Append((byte)0xFF)
			.Concat(LinkFrame.Close(2).Encode())
			.ToArray();

		reader.Append(bytes, _start, output);

		Assert.That(output.Select(item => item.Frame.Type),
			Is.EqualTo(new[] { LinkFrameType.Hello, LinkFrameType.Open, LinkFrameType.Close }));
	}

	[Test]
	public void An_unknown_type_a_bad_hello_or_an_oversized_length_resyncs_on_the_next_hello()
	{
		byte[][] malformed =
		[
			[9, 0, 0, 1, 0, 0, 0, 0],
			[0, 0, 0, 0, 0, 0, 0, 13, (byte)'N', (byte)'O', (byte)'P', (byte)'E', 1, 0, 0, 0, 1, 0, 0, 0, 0],
			[2, 0, 0, 1, 0, 0, 0x40, 0],
			[0, 0, 0, 0, 0, 0, 0, 12, (byte)'M', (byte)'D', (byte)'L', (byte)'K', 1, 0, 0, 0, 1, 0, 0, 0]
		];

		Assert.Multiple(() =>
		{
			foreach (var bad in malformed)
			{
				var reader = new LinkFrameReader();
				var output = new List<LinkInbound>();
				reader.Append(LinkFrame.Hello(false, 1, 0).Encode(), _start, output);
				reader.Append(bad.Concat(LinkFrame.Hello(false, 2, 0).Encode()).ToArray(), _start, output);

				Assert.That(output.Select(item => item.Resync ? "resync" : item.Frame.Type.ToString()),
					Is.EqualTo(new[] { "Hello", "resync", "Hello" }));
				Assert.That(LinkHello.Parse(output[2].Frame).Epoch, Is.EqualTo(2u));
			}
		});
	}

	[Test]
	public void Half_a_frame_followed_by_hellos_resent_every_second_resyncs_two_seconds_after_its_first_byte()
	{
		var reader = new LinkFrameReader();
		var output = new List<LinkInbound>();
		reader.Append(LinkFrame.Hello(false, 1, 0).Encode(), _start, output);
		output.Clear();

		reader.Append(LinkFrame.Data(4, new byte[400]).Encode().AsSpan(0, 30), _start, output);
		reader.Append(LinkFrame.Hello(false, 1, 0).Encode(), _start.AddSeconds(1), output);
		var stuckAfterOneSecond = reader.IsStuck(_start.AddSeconds(1.5));
		reader.Append(LinkFrame.Hello(false, 1, 0).Encode(), _start.AddSeconds(2), output);
		var stuckAfterTwoSeconds = reader.IsStuck(_start.AddSeconds(2));
		reader.Resync(_start.AddSeconds(2), output);

		Assert.Multiple(() =>
		{
			Assert.That(stuckAfterOneSecond, Is.False);
			Assert.That(stuckAfterTwoSeconds, Is.True);
			Assert.That(output.Select(item => item.Resync ? "resync" : item.Frame.Type.ToString()),
				Is.EqualTo(new[] { "resync", "Hello", "Hello" }));
		});
	}

	[Test]
	public void A_frame_still_completing_is_not_stuck_before_two_seconds_even_across_reads()
	{
		var reader = new LinkFrameReader();
		var output = new List<LinkInbound>();
		reader.Append(LinkFrame.Hello(false, 1, 0).Encode(), _start, output);
		var frame = LinkFrame.Data(4, new byte[400]).Encode();

		reader.Append(frame.AsSpan(0, 100), _start, output);
		var stuck = reader.IsStuck(_start.AddSeconds(1.9));
		reader.Append(frame.AsSpan(100), _start.AddSeconds(1.9), output);

		Assert.Multiple(() =>
		{
			Assert.That(stuck, Is.False);
			Assert.That(reader.IsStuck(_start.AddSeconds(10)), Is.False);
			Assert.That(output.Last().Frame.Type, Is.EqualTo(LinkFrameType.Data));
		});
	}
}
