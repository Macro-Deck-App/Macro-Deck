using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Integrations.Ping.Connections;
using MacroDeckHost.Integrations.Ping.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Ping.Widget;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal sealed record PingViewState
{
	public bool HasTarget { get; init; }

	public string Title { get; init; } = string.Empty;

	public PingReachability Reachability { get; init; }

	public LocalizedText Value { get; init; }

	public LocalizedText Detail { get; init; }

	public IReadOnlyList<double> Points { get; init; } = [];

	public bool HasChart => Points.Count >= 2;

	public bool Equals(PingViewState? other)
	{
		return other is not null
			&& HasTarget == other.HasTarget
			&& Title == other.Title
			&& Reachability == other.Reachability
			&& Value.Equals(other.Value)
			&& Detail.Equals(other.Detail)
			&& Points.SequenceEqual(other.Points);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(HasTarget, Title, Reachability, Value, Detail, Points.Count);
	}
}

internal static class PingViewStateResolver
{
	public const int HistoryPings = 30;

	public const int MaxHistorySamples = 600;

	public static PingSnapshot? Target(PingWidgetOptions options, IPingTargets targets)
	{
		return options.TargetId is { } id
			? targets.Find(id)
			: targets.Targets is [var first, ..]
				? first
				: null;
	}

	public static int HistoryCapacity(PingSnapshot snapshot)
	{
		var seconds = (int)Math.Ceiling(snapshot.Interval.TotalSeconds);
		return Math.Clamp(seconds * HistoryPings, HistoryPings, MaxHistorySamples);
	}

	public static PingViewState Resolve(
		PingWidgetOptions options,
		PingSnapshot? snapshot,
		IReadOnlyList<double?> samples)
	{
		if (snapshot is null)
		{
			return new PingViewState
			{
				Title = options.DisplayName ?? string.Empty, Value = Strings.NoTarget()
			};
		}

		var online = snapshot.IsOnline;
		var showLatency = online && options.ShowLatency && snapshot.LatencyMilliseconds is not null;
		var status = StatusText(snapshot);
		LocalizedText detail = default;
		if (snapshot.Problem == PingProblem.NotPermitted)
		{
			detail = snapshot.Reachability == PingReachability.Unknown ? default : Strings.NotPermitted();
		}
		else if (online && options.ShowPacketLoss && snapshot.PacketLossPercent is { } loss)
		{
			detail = Strings.PacketLoss(loss.ToString("0.#", CultureInfo.CurrentCulture));
		}
		else if (showLatency)
		{
			detail = status;
		}
		else if (!online && snapshot.Problem != PingProblem.None)
		{
			detail = ProblemText(snapshot.Problem);
		}

		return new PingViewState
		{
			HasTarget = true,
			Title = options.DisplayName ?? snapshot.Title,
			Reachability = snapshot.Reachability,
			Value = showLatency
				? Strings.Latency(snapshot.LatencyMilliseconds!.Value.ToString("N0", CultureInfo.CurrentCulture))
				: status,
			Detail = detail,
			Points = options.ShowHistory ? Normalize(samples, HistoryCapacity(snapshot)) : []
		};
	}

	internal static IReadOnlyList<double> Normalize(IReadOnlyList<double?> samples, int capacity)
	{
		double[] values = [.. samples.TakeLast(capacity).OfType<double>()];
		if (values.Length < 2)
		{
			return [];
		}

		var high = values.Max();
		return [.. values.Select(sample => high > 0 ? Math.Round(Math.Clamp(sample / high, 0, 1), 4) : 0)];
	}

	private static LocalizedText StatusText(PingSnapshot snapshot)
	{
		return snapshot.Reachability switch
		{
			PingReachability.Online => Strings.Online(),
			PingReachability.Offline => Strings.Offline(),
			_ => snapshot.Problem == PingProblem.NotPermitted ? Strings.NotPermitted() : Strings.Waiting()
		};
	}

	private static LocalizedText ProblemText(PingProblem problem)
	{
		return problem switch
		{
			PingProblem.TimedOut => Strings.TimedOut(),
			PingProblem.Unreachable => Strings.Unreachable(),
			PingProblem.NameNotResolved => Strings.NameNotResolved(),
			PingProblem.NotPermitted => Strings.NotPermitted(),
			_ => Strings.Failed()
		};
	}
}
