using System.Collections.Concurrent;
using MacroDeck.Sdk.FolderViews;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>Which <see cref="IFolderViewProviderContext" /> method a <see cref="FolderViewProviderCall" />
/// recorded.</summary>
public enum FolderViewProviderCallKind
{
	/// <summary>Recorded by <see cref="IFolderViewProviderContext.RegisterFolderViewAsync" />.</summary>
	Register,

	/// <summary>Recorded by <see cref="IFolderViewProviderContext.UnregisterFolderViewAsync" />.</summary>
	Unregister
}

/// <summary>One call recorded by <see cref="FakeFolderViewProviderContext" />.</summary>
public sealed record FolderViewProviderCall
{
	/// <summary>Which method was called.</summary>
	public required FolderViewProviderCallKind Kind { get; init; }

	/// <summary>The provider-local folder view id the call addressed.</summary>
	public required string FolderViewId { get; init; }

	/// <summary>The descriptor, for a register call.</summary>
	public FolderViewDescriptor? FolderView { get; init; }
}

/// <summary>
/// In-memory <see cref="IFolderViewProviderContext" />, standing in for the host's folder view registry.
/// It keeps the same identity rules the host has - a re-registration under a known provider-local id
/// replaces the existing view rather than duplicating it, and unregistering an unknown id is a silent
/// no-op - so a provider tested against it sees the same behaviour it would see for real.
/// </summary>
public sealed class FakeFolderViewProviderContext : IFolderViewProviderContext
{
	private readonly ConcurrentDictionary<string, FolderViewDescriptor> _folderViews = new(StringComparer.Ordinal);
	private readonly List<FolderViewProviderCall> _calls = [];
	private readonly Lock _sync = new();

	/// <summary>Every call made against this context, in order.</summary>
	public IReadOnlyList<FolderViewProviderCall> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	/// <summary>The folder views currently registered, keyed by their provider-local id.</summary>
	public IReadOnlyDictionary<string, FolderViewDescriptor> FolderViews => _folderViews;

	public Task<FolderViewRegistration> RegisterFolderViewAsync(
		FolderViewDescriptor folderView,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(folderView);
		ArgumentException.ThrowIfNullOrWhiteSpace(folderView.Id);

		if (folderView.Name.IsEmpty)
		{
			throw new ArgumentException("The folder view name must not be empty.", nameof(folderView));
		}

		// Reusing the id a previous registration was given is what makes this a replace rather than a new
		// view - the host resolves the very same way, by (provider, provider-local id).
		_folderViews[folderView.Id] = folderView;

		lock (_sync)
		{
			_calls.Add(new FolderViewProviderCall
			{
				Kind = FolderViewProviderCallKind.Register, FolderViewId = folderView.Id, FolderView = folderView
			});
		}

		return Task.FromResult(new FolderViewRegistration(folderView.Id, "test-provider"));
	}

	public Task UnregisterFolderViewAsync(string folderViewId, CancellationToken cancellationToken = default)
	{
		_folderViews.TryRemove(folderViewId, out _);

		lock (_sync)
		{
			_calls.Add(new FolderViewProviderCall
			{
				Kind = FolderViewProviderCallKind.Unregister, FolderViewId = folderViewId
			});
		}

		return Task.CompletedTask;
	}
}
