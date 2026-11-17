using System.Runtime.Versioning;
using MacroDeckHost.Integrations.Keyboard.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.Keyboard;

[Platform("Linux")]
[SupportedOSPlatform("linux")]
public class LinuxCapsLockTypingTests
{
	private const uint Lock = 1 << 1;
	private const uint NumLock = 1 << 4;

	private static readonly uint[] _typedOnceWithNumLock = [NumLock];
	private static readonly uint[] _typedOnceWithCapsLock = [Lock];

	[Test]
	public void Typing_with_caps_lock_off_never_requests_a_lock_change()
	{
		var keyboard = new FakeKeyboard(NumLock);

		keyboard.Type();

		Assert.Multiple(() =>
		{
			Assert.That(keyboard.LockedWhileTyping, Is.EqualTo(_typedOnceWithNumLock));
			Assert.That(keyboard.LockRequests, Is.Zero);
			Assert.That(keyboard.LockedModifiers, Is.EqualTo(NumLock));
		});
	}

	[Test]
	public void Typing_with_caps_lock_on_types_without_it_and_leaves_it_on()
	{
		var keyboard = new FakeKeyboard(Lock | NumLock);

		keyboard.Type();

		Assert.Multiple(() =>
		{
			Assert.That(keyboard.LockedWhileTyping, Is.EqualTo(_typedOnceWithNumLock));
			Assert.That(keyboard.LockedModifiers, Is.EqualTo(Lock | NumLock));
		});
	}

	[Test]
	public void Caps_lock_is_on_again_when_typing_fails()
	{
		var keyboard = new FakeKeyboard(Lock | NumLock);

		Assert.Throws<InvalidOperationException>(() => keyboard.Type(() => throw new InvalidOperationException()));

		Assert.That(keyboard.LockedModifiers, Is.EqualTo(Lock | NumLock));
	}

	[Test]
	public void Text_is_still_typed_when_caps_lock_cannot_be_released()
	{
		var keyboard = new FakeKeyboard(Lock, true);

		keyboard.Type();

		Assert.Multiple(() =>
		{
			Assert.That(keyboard.LockedWhileTyping, Is.EqualTo(_typedOnceWithCapsLock));
			Assert.That(keyboard.LockedModifiers, Is.EqualTo(Lock));
		});
	}

	private sealed class FakeKeyboard
	{
		private readonly bool _ignoresLockRequests;

		public FakeKeyboard(uint lockedModifiers, bool ignoresLockRequests = false)
		{
			LockedModifiers = lockedModifiers;
			_ignoresLockRequests = ignoresLockRequests;
		}

		public uint LockedModifiers { get; private set; }

		public int LockRequests { get; private set; }

		public List<uint> LockedWhileTyping { get; } = [];

		public void Type(Action? typing = null)
		{
			LinuxKeyboardInputProvider.TypeWithCapsLockReleased(LockedModifiers,
				LockModifiers,
				() => LockedModifiers,
				() =>
				{
					LockedWhileTyping.Add(LockedModifiers);
					typing?.Invoke();
				});
		}

		private void LockModifiers(uint affect, uint values)
		{
			LockRequests++;
			if (!_ignoresLockRequests)
			{
				LockedModifiers = LockedModifiers & ~affect | values & affect;
			}
		}
	}
}
