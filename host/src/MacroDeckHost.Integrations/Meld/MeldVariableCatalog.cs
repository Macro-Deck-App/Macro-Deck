using System.Globalization;
using System.Text;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Meld;

/// <summary>
/// The catalog half of <see cref="MeldIntegration"/>'s variable provider: one node per Meld Studio audio
/// track, carrying its gain, mute and monitoring state. A track set is small but entirely runtime data
/// that changes with the session, so it is browsed and bound rather than registered up front alongside
/// the integration's eight fixed variables.
/// </summary>
internal sealed class MeldVariableCatalog
{
	private const string TrackSegment = "track";
	private const string GainLeaf = "gain";
	private const string MutedLeaf = "muted";
	private const string MonitoringLeaf = "monitoring";

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(1);

	private readonly Func<MeldConnection?> _connection;

	public MeldVariableCatalog(Func<MeldConnection?> connection)
	{
		_connection = connection;
	}

	public static string CatalogName => "Meld Studio";

	// Meld pushes gain updates but nothing for mute or monitoring, and SupportsPush is all-or-nothing per
	// provider: reporting true would turn off polling for the leaves it never pushes, freezing them at
	// their first value.
	public static bool SupportsPush => false;

	// A session has a handful of tracks, so the host correctly omits the search box rather than offering
	// one that filters a single already-loaded page.
	public static bool SupportsSearch => false;

	/// <summary>
	/// Three leaves per track. Answered from the session Meld already pushed, so counting costs
	/// nothing - unlike a provider that would have to enumerate its resources, which reports null.
	/// </summary>
	public int? CatalogEntryCount => (_connection()?.State.Session.Tracks.Count ?? 0) * LeavesPerTrack;

	private const int LeavesPerTrack = 3;

	public ValueTask<VariableCatalogPage> DiscoverAsync(
		VariableCatalogQuery query,
		CancellationToken cancellationToken = default)
	{
		var session = _connection()?.State.Session ?? MeldSession.Empty;

		if (query.ParentId is null)
		{
			var candidates = session.Tracks
				.Select(track => (Id: TrackId(track.Id), Definition: TrackContainer(track.Id, track.Name)))
				.ToList();

			return ValueTask.FromResult(BuildPage(candidates, query));
		}

		if (Parse(query.ParentId) is not { Leaf: null } parent || !session.TracksById.ContainsKey(parent.TrackId))
		{
			return ValueTask.FromResult(VariableCatalogPage.Empty);
		}

		var name = session.TracksById[parent.TrackId].Name;
		var leaves = new List<(string Id, VariableDefinition Definition)>
		{
			(LeafId(parent.TrackId, GainLeaf), GainDefinition(parent.TrackId, name)),
			(LeafId(parent.TrackId, MutedLeaf), MutedDefinition(parent.TrackId, name)),
			(LeafId(parent.TrackId, MonitoringLeaf), MonitoringDefinition(parent.TrackId, name))
		};

		return ValueTask.FromResult(BuildPage(leaves, query));
	}

	/// <summary>
	/// Answers from the id's own shape, never from the live session: Meld Studio is a desktop application
	/// the user closes and reopens all day, and resolving against live state would report every saved
	/// binding as a broken reference the moment it is not running.
	/// </summary>
	public ValueTask<VariableDefinition?> ResolveAsync(string id, CancellationToken cancellationToken = default)
	{
		if (Parse(id) is not { } parsed)
		{
			return ValueTask.FromResult<VariableDefinition?>(null);
		}

		var name = _connection()?.State.Session.TracksById.GetValueOrDefault(parsed.TrackId)?.Name;

		return ValueTask.FromResult<VariableDefinition?>(parsed.Leaf switch
		{
			null => TrackContainer(parsed.TrackId, name),
			GainLeaf => GainDefinition(parsed.TrackId, name),
			MutedLeaf => MutedDefinition(parsed.TrackId, name),
			MonitoringLeaf => MonitoringDefinition(parsed.TrackId, name),
			_ => null
		});
	}

	public ValueTask<VariableReading> ReadAsync(string id, CancellationToken cancellationToken = default)
	{
		var connection = _connection();
		if (Parse(id) is not { Leaf: { } leaf } parsed ||
			connection is null ||
			!connection.State.IsConnected ||
			!connection.State.Session.TracksById.TryGetValue(parsed.TrackId, out var track))
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		var hasGain = connection.TryGetGain(parsed.TrackId, out var gain);

		return ValueTask.FromResult(leaf switch
		{
			GainLeaf => hasGain
				? VariableReading.Of(Math.Round(gain.Gain * 100d), 0, 100, 1)
				: VariableReading.Unavailable,
			MutedLeaf => VariableReading.Of(hasGain ? gain.Muted : track.Muted),
			MonitoringLeaf => VariableReading.Of(track.Monitoring),
			_ => VariableReading.Unavailable
		});
	}

	public async ValueTask<VariableWriteResult> SetValueAsync(
		string id,
		object? value,
		CancellationToken cancellationToken = default)
	{
		if (Parse(id) is not { Leaf: GainLeaf } parsed)
		{
			return VariableWriteResult.NotWritable();
		}

		if (_connection() is not { } connection)
		{
			return VariableWriteResult.Unavailable();
		}

		if (!TryReadNumber(value, out var percent))
		{
			return VariableWriteResult.InvalidValue();
		}

		var result = await connection
			.SetGainAsync(parsed.TrackId, Math.Clamp(percent, 0, 100) / 100.0, cancellationToken)
			.ConfigureAwait(false);

		// Accepted counts as applied: Meld took the call and only the confirmation pulse timed out, and
		// the authoritative gain arrives on the read side either way.
		if (result.Status != ActionResultStatus.Failed)
		{
			return VariableWriteResult.Applied();
		}

		return result.ErrorCode switch
		{
			ActionErrorCodes.NotConnected => VariableWriteResult.Unavailable(result.ErrorMessage),
			ActionErrorCodes.NotFound => VariableWriteResult.NotFound(result.ErrorMessage),
			_ => VariableWriteResult.Failed(result.ErrorMessage)
		};
	}

	// The token is the last candidate on this page, so the next page starts strictly after it.
	private static VariableCatalogPage BuildPage(
		List<(string Id, VariableDefinition Definition)> candidates,
		VariableCatalogQuery query)
	{
		candidates.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));

		var startIndex = 0;
		if (query.ContinuationToken is { Length: > 0 } token)
		{
			while (startIndex < candidates.Count && string.CompareOrdinal(candidates[startIndex].Id, token) <= 0)
			{
				startIndex++;
			}
		}

		var slice = candidates.Skip(startIndex).Take(Math.Max(query.PageSize, 0)).ToList();
		var hasMore = startIndex + slice.Count < candidates.Count;

		return new VariableCatalogPage
		{
			Items = slice.Select(candidate => candidate.Definition).ToList(),
			ContinuationToken = hasMore ? slice[^1].Id : null
		};
	}

	private static VariableDefinition TrackContainer(string trackId, string? name)
		=> VariableDefinition.OnDemand(TrackId(trackId), VariableType.Text) with
		{
			Name = SuggestedName(trackId, name, null),
			DisplayName = name is { Length: > 0 }
				? name
				: AppStrings.Integrations.Meld.VariableCatalog.Track(),
			IsContainer = true,
			IsBindable = false
		};

	private static VariableDefinition GainDefinition(string trackId, string? name)
		=> VariableDefinition.OnDemand(LeafId(trackId, GainLeaf), VariableType.Numeric) with
		{
			Name = SuggestedName(trackId, name, GainLeaf),
			DisplayName = AppStrings.Integrations.Meld.VariableCatalog.Gain(),
			ParentId = TrackId(trackId),
			DecimalPlaces = 0,
			RefreshInterval = _refreshInterval,
			Unit = "%",
			SemanticKind = VariableSemanticKinds.Percentage,
			Write = new VariableWriteCapability()
		};

	private static VariableDefinition MutedDefinition(string trackId, string? name)
		=> VariableDefinition.OnDemand(LeafId(trackId, MutedLeaf), VariableType.Boolean) with
		{
			Name = SuggestedName(trackId, name, MutedLeaf),
			DisplayName = AppStrings.Integrations.Meld.VariableCatalog.Muted(),
			ParentId = TrackId(trackId),
			RefreshInterval = _refreshInterval
		};

	private static VariableDefinition MonitoringDefinition(string trackId, string? name)
		=> VariableDefinition.OnDemand(LeafId(trackId, MonitoringLeaf), VariableType.Boolean) with
		{
			Name = SuggestedName(trackId, name, MonitoringLeaf),
			DisplayName = AppStrings.Integrations.Meld.VariableCatalog.Monitoring(),
			ParentId = TrackId(trackId),
			RefreshInterval = _refreshInterval
		};

	private static string TrackId(string trackId) => $"{TrackSegment}/{CatalogResourceIds.Encode(trackId)}";

	private static string LeafId(string trackId, string leaf) => $"{TrackId(trackId)}/{leaf}";

	private static string SuggestedName(string trackId, string? name, string? leaf)
	{
		var head = "meld_" + Sanitize(name is { Length: > 0 } ? name : trackId);
		return leaf is null ? head : head + "_" + leaf;
	}

	private static string Sanitize(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var character in value.ToLowerInvariant())
		{
			builder.Append(char.IsAsciiLetterLower(character) || char.IsAsciiDigit(character) ? character : '_');
		}

		return builder.ToString();
	}

	private static ParsedId? Parse(string id)
	{
		var segments = id.Split('/');
		if (segments.Length is not (2 or 3) ||
			segments[0] != TrackSegment ||
			!CatalogResourceIds.TryDecode(segments[1], out var trackId) ||
			trackId.Length == 0)
		{
			return null;
		}

		if (segments.Length == 2)
		{
			return new ParsedId(trackId, null);
		}

		return segments[2] is GainLeaf or MutedLeaf or MonitoringLeaf
			? new ParsedId(trackId, segments[2])
			: null;
	}

	private static bool TryReadNumber(object? value, out double number)
	{
		number = value switch
		{
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => double.NaN
		};

		return double.IsFinite(number);
	}

	private readonly record struct ParsedId(string TrackId, string? Leaf);
}
