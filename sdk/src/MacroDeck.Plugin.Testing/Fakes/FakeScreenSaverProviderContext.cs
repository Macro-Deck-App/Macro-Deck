using System.Collections.Concurrent;
using MacroDeck.Sdk.ScreenSavers;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="IScreenSaverProviderContext" /> method a <see cref="ScreenSaverProviderCall" />
/// recorded.</summary>
public enum ScreenSaverProviderCallKind
{
	/// <summary>Recorded by <see cref="IScreenSaverProviderContext.RegisterScreenSaverAsync" />.</summary>
	Register,

	/// <summary>Recorded by <see cref="IScreenSaverProviderContext.UnregisterScreenSaverAsync" />.</summary>
	Unregister
}

/// <summary>One call recorded by <see cref="FakeScreenSaverProviderContext" />.</summary>
public sealed record ScreenSaverProviderCall
{
	/// <summary>Which method was called.</summary>
	public required ScreenSaverProviderCallKind Kind { get; init; }

	/// <summary>The provider-local screensaver id the call addressed.</summary>
	public required string ScreenSaverId { get; init; }

	/// <summary>The descriptor, for a register call.</summary>
	public ScreenSaverDescriptor? ScreenSaver { get; init; }
}

/// <summary>
/// In-memory <see cref="IScreenSaverProviderContext" />, standing in for the host's screensaver registry
/// with the host's own identity rules: a re-registration under a known provider-local id replaces the
/// existing screensaver, and unregistering an unknown id is a silent no-op.
/// </summary>
public sealed class FakeScreenSaverProviderContext : IScreenSaverProviderContext
{
	private readonly ConcurrentDictionary<string, ScreenSaverDescriptor> _screenSavers = new(StringComparer.Ordinal);
	private readonly List<ScreenSaverProviderCall> _calls = [];
	private readonly Lock _sync = new();

	/// <summary>Every call made against this context, in order.</summary>
	public IReadOnlyList<ScreenSaverProviderCall> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>The screensavers currently registered, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, ScreenSaverDescriptor> ScreenSavers => _screenSavers;

	public Task<ScreenSaverRegistration> RegisterScreenSaverAsync(
		ScreenSaverDescriptor screenSaver,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(screenSaver);
		ArgumentException.ThrowIfNullOrWhiteSpace(screenSaver.Id);

		if (screenSaver.Name.IsEmpty)
		{
			throw new ArgumentException("The screensaver name must not be empty.", nameof(screenSaver));
		}

		_screenSavers[screenSaver.Id] = screenSaver;

		lock (_sync)
		{
			_calls.Add(new ScreenSaverProviderCall
			{
				Kind = ScreenSaverProviderCallKind.Register, ScreenSaverId = screenSaver.Id, ScreenSaver = screenSaver
			});
		}

		return Task.FromResult(new ScreenSaverRegistration(screenSaver.Id, "test-provider"));
	}

	public Task UnregisterScreenSaverAsync(string screenSaverId, CancellationToken cancellationToken = default)
	{
		_screenSavers.TryRemove(screenSaverId, out _);

		lock (_sync)
		{
			_calls.Add(new ScreenSaverProviderCall
			{
				Kind = ScreenSaverProviderCallKind.Unregister, ScreenSaverId = screenSaverId
			});
		}

		return Task.CompletedTask;
	}
}
