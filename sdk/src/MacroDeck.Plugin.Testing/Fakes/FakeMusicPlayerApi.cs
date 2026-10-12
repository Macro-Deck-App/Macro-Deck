using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IMusicPlayerApi" />. Seed the players a plugin under test reads with <see cref="Set" />,
/// which raises <see cref="InstancesChanged" /> before it returns, and their album colours with
/// <see cref="SetAlbumColor" />.
/// </summary>
public sealed class FakeMusicPlayerApi : IMusicPlayerApi
{
	private readonly Lock _sync = new();
	private readonly Dictionary<string, string> _colors = new(StringComparer.Ordinal);
	private IReadOnlyList<MusicPlayerInstanceSnapshot> _instances = [];

	/// <inheritdoc />
	public IReadOnlyList<MusicPlayerInstanceSnapshot> Instances
	{
		get
		{
			lock (_sync)
			{
				return _instances;
			}
		}
	}

	/// <inheritdoc />
	public event EventHandler? InstancesChanged;

	/// <summary>The number of handlers currently subscribed to <see cref="InstancesChanged" />.</summary>
	public int SubscriberCount => InstancesChanged?.GetInvocationList().Length ?? 0;

	/// <summary>Replaces the players and raises <see cref="InstancesChanged" />.</summary>
	public void Set(IEnumerable<MusicPlayerInstanceSnapshot> instances)
	{
		ArgumentNullException.ThrowIfNull(instances);

		lock (_sync)
		{
			_instances = [.. instances];
		}

		InstancesChanged?.Invoke(this, EventArgs.Empty);
	}

	/// <summary>Sets the colour <see cref="GetAlbumColorAsync" /> answers for a player, or forgets it with <c>null</c>.</summary>
	public void SetAlbumColor(string instanceId, string? color)
	{
		ArgumentException.ThrowIfNullOrEmpty(instanceId);

		lock (_sync)
		{
			if (color is null)
			{
				_colors.Remove(instanceId);
			}
			else
			{
				_colors[instanceId] = color;
			}
		}
	}

	/// <inheritdoc />
	public Task<string?> GetAlbumColorAsync(string instanceId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(instanceId);
		cancellationToken.ThrowIfCancellationRequested();

		lock (_sync)
		{
			return Task.FromResult(_colors.GetValueOrDefault(instanceId));
		}
	}
}
