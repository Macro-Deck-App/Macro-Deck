using System.Collections.Concurrent;
using MacroDeck.Sdk.Layouts;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="ILayoutProviderContext" /> method a <see cref="LayoutProviderCall" /> recorded.</summary>
public enum LayoutProviderCallKind
{
	/// <summary>Recorded by <see cref="ILayoutProviderContext.RegisterLayoutAsync" />.</summary>
	Register,

	/// <summary>Recorded by <see cref="ILayoutProviderContext.UnregisterLayoutAsync" />.</summary>
	Unregister
}

/// <summary>One call recorded by <see cref="FakeLayoutProviderContext" />.</summary>
public sealed record LayoutProviderCall
{
	/// <summary>Which method was called.</summary>
	public required LayoutProviderCallKind Kind { get; init; }

	/// <summary>The provider-local layout id the call addressed.</summary>
	public required string LayoutId { get; init; }

	/// <summary>The descriptor, for a register call.</summary>
	public LayoutDescriptor? Layout { get; init; }
}

/// <summary>
/// In-memory <see cref="ILayoutProviderContext" />, standing in for the host's layout registry. It keeps
/// the same identity rules the host has - a re-registration under a known provider-local id replaces the
/// existing layout rather than duplicating it, and unregistering an unknown id is a silent no-op - so a
/// provider tested against it sees the same behaviour it would see for real.
/// </summary>
public sealed class FakeLayoutProviderContext : ILayoutProviderContext
{
	private readonly ConcurrentDictionary<string, LayoutDescriptor> _layouts = new(StringComparer.Ordinal);
	private readonly List<LayoutProviderCall> _calls = [];
	private readonly Lock _sync = new();

	/// <summary>Every call made against this context, in order.</summary>
	public IReadOnlyList<LayoutProviderCall> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>The layouts currently registered, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, LayoutDescriptor> Layouts => _layouts;

	public Task<LayoutRegistration> RegisterLayoutAsync(
		LayoutDescriptor layout,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(layout);
		ArgumentException.ThrowIfNullOrWhiteSpace(layout.Id);
		ArgumentException.ThrowIfNullOrWhiteSpace(layout.Name);

		var seenRegionIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (var region in layout.Regions)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(region.Id);

			if (!seenRegionIds.Add(region.Id))
			{
				throw new ArgumentException($"Region id '{region.Id}' is repeated within the layout.",
					nameof(layout));
			}
		}

		// Reusing the id a previous registration was given is what makes this a replace rather than a new
		// layout - the host resolves the very same way, by (provider, provider-local id).
		_layouts[layout.Id] = layout;

		lock (_sync)
		{
			_calls.Add(new LayoutProviderCall
				{ Kind = LayoutProviderCallKind.Register, LayoutId = layout.Id, Layout = layout });
		}

		return Task.FromResult(new LayoutRegistration(layout.Id, "test-provider"));
	}

	public Task UnregisterLayoutAsync(string layoutId, CancellationToken cancellationToken = default)
	{
		_layouts.TryRemove(layoutId, out _);

		lock (_sync)
		{
			_calls.Add(new LayoutProviderCall { Kind = LayoutProviderCallKind.Unregister, LayoutId = layoutId });
		}

		return Task.CompletedTask;
	}
}
