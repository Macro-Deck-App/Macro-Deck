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
	public static PingViewState Resolve(PingWidgetOptions options, IPingTargets targets)
	{
		var snapshot = options.TargetId is { } id
			? targets.Find(id)
			: targets.Targets is [var first, ..]
				? first
				: null;
		return Resolve(options, snapshot);
	}

	public static PingViewState Resolve(PingWidgetOptions options, PingSnapshot? snapshot)
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
		else if (online && options.ShowPacketLoss && snapshot.PacketLossPercent is > 0 and var loss)
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
			Points = options.ShowHistory ? Normalize(snapshot.LatencyHistory) : []
		};
	}

	internal static IReadOnlyList<double> Normalize(IReadOnlyList<double> samples)
	{
		if (samples.Count < 2)
		{
			return [];
		}

		var low = samples.Min();
		var high = samples.Max();
		var range = high - low;
		return [.. samples.Select(sample => range > 0 ? Math.Round((sample - low) / range, 4) : 0.5)];
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
