using System.Collections.Concurrent;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using static MacroDeck.Ui.Tests.UnitTests.Runtime.UiConcurrency;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// Regression coverage for the filter race issue #830 also names: the query a load asks for is written on
/// whichever thread dispatched the filter change and read on whichever thread starts the load, so a torn
/// read would fetch options for a filter nobody ever set and show them for one they did. Every verdict is
/// taken after the racing threads have joined; the concurrency only builds the state.
/// </summary>
[TestFixture]
public class UiOptionsStateConcurrencyTests
{
	private const string _optionsNodeKey = "options";

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiNode? FindById(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (FindById(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	/// <summary>The ids of the rows the repeat rendered, which is the option list as a client sees it.
	/// </summary>
	private static IReadOnlyList<string> RenderedOptions(UiView view)
	{
		var node = FindById(view.Tree.Root, "setup." + _optionsNodeKey);

		Assert.That(node, Is.Not.Null, "the fixture has to render the option list for this to prove anything");

		return [.. node!.Children.Select(child => child.Id)];
	}

	/// <summary>A flow whose option rows are real nodes, so "what the client sees" is the tree rather than a
	/// property nobody renders.</summary>
	private static UiView OptionListView(UiOptionsState options, UiState<string> typed)
		=> new(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "search", Binding = Bind.To(typed) },
					new UiConfigStack
					{
						Key = _optionsNodeKey,
						Children =
						[
							new UiRepeat<UiOption>
							{
								Key = "rows",
								Items = UiValue.From(() => options.Options),
								KeySelector = option => option.Value,
								Template = (option, _) => new UiStringInput
								{
									Key = option.Value,
									Binding = Bind.ReadOnly(UiValue.Of(option.Value)),
								},
							},
						],
					},
				],
			});

	[Test]
	[CancelAfter(30_000)]
	public void A_load_always_asks_for_a_query_that_was_actually_set_and_the_last_one_set_wins()
	{
		const int iterations = 500;
		const string finalFilter = "final";

		var seen = new List<UiOptionQuery>();
		var recorded = new Lock();
		var failures = new ConcurrentBag<Exception>();

		var source = UiOptionSource.From((query, _) =>
		{
			lock (recorded)
			{
				seen.Add(query);
			}

			return Task.FromResult(UiOptionResult.From([]));
		});

		var options = new UiOptionsState(source);

		// The instances the test hands in, plus the one the state started with: nothing else may ever come
		// back out of a query a load was given.
		var initialValues = options.Query.Values;
		var pool = new IReadOnlyDictionary<string, string>[4];

		for (var index = 0; index < pool.Length; index++)
		{
			pool[index] = new Dictionary<string, string>(StringComparer.Ordinal) { [$"sibling{index}"] = $"v{index}" };
		}

		var finalValues = new Dictionary<string, string>(StringComparer.Ordinal) { ["sibling"] = "final" };
		var writtenFilters = new HashSet<string?>(StringComparer.Ordinal) { null, "a", finalFilter };

		for (var index = 0; index < iterations; index++)
		{
			writtenFilters.Add($"b{index}");
		}

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					options.SetFilter("a");
					options.Reload();
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					options.SetFilter($"b{index}");
					options.SetValues(pool[index % pool.Length]);
				}
			});

		AssertNoWorkerFaulted(failures);

		options.SetValues(finalValues);
		options.SetFilter(finalFilter);
		options.Reload();

		UiOptionQuery[] queries;

		lock (recorded)
		{
			queries = [.. seen];
		}

		Assert.That(queries, Is.Not.Empty);

		Assert.Multiple(() =>
		{
			foreach (var query in queries)
			{
				Assert.That(writtenFilters, Does.Contain(query.Filter), "a load asked for a filter nobody set");

				// Reference equality, not equality: a torn read that rebuilt a dictionary would compare equal
				// to one of these and still be a value the writer never handed over as a unit.
				Assert.That(pool.Concat([finalValues, initialValues])
						.Any(instance => ReferenceEquals(instance, query.Values)),
					Is.True,
					"a load asked for sibling values nobody set");
			}
		});

		Assert.Multiple(() =>
		{
			Assert.That(queries[^1].Filter, Is.EqualTo(finalFilter));
			Assert.That(queries[^1].Values, Is.SameAs(finalValues));
			Assert.That(options.Query.Filter, Is.EqualTo(finalFilter));
			Assert.That(options.Query.Values, Is.SameAs(finalValues));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public async Task The_options_a_client_sees_match_a_query_that_was_asked_for()
	{
		const int iterations = 200;
		const string finalFilter = "final";

		var failures = new ConcurrentBag<Exception>();

		var source = UiOptionSource.From((query, _) => Task.FromResult(UiOptionResult.From(OptionsFor(query.Filter))));

		var options = new UiOptionsState(source);
		var typed = new UiState<string>(string.Empty);
		var view = OptionListView(options, typed);
		var initial = view.Tree;
		var initialRows = RenderedOptions(view);

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					options.SetFilter($"f{index}");
					options.Reload();
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					var result = view.Dispatch(new Model.Events.UiEvent
					{
						NodeId = "search",
						Name = UiConfigEvents.Change,
						Data = UiCanonicalJson.ToElement($"typed{index}"),
					});

					if (!result.IsAccepted)
					{
						throw new InvalidOperationException($"the dispatcher was refused: {result.Reason}");
					}
				}
			});

		AssertNoWorkerFaulted(failures);

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await view.WhenIdleAsync(cancellation.Token);

		// The deterministic tail: whatever the storm left behind, the last load asked for is what the client
		// ends up looking at.
		options.SetFilter(finalFilter);
		options.Reload();

		await view.WhenIdleAsync(cancellation.Token);

		var replayed = initial;

		foreach (var patch in view.DrainPatches())
		{
			var applied = UiTreeApplier.Apply(replayed, patch);

			Assert.That(applied.IsApplied, Is.True, applied.RejectionReason);

			replayed = applied.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(replayed), Is.EqualTo(UiCanonicalJson.Serialize(view.Tree)));
			Assert.That(RenderedOptions(view),
				Is.EqualTo(OptionsFor(finalFilter).Select(option => option.Value)).AsCollection);
			Assert.That(options.Options.Select(option => option.Value),
				Is.EqualTo(OptionsFor(finalFilter).Select(option => option.Value)).AsCollection);
			Assert.That(options.IsLoading, Is.False);
			Assert.That(options.Error, Is.Null);
			Assert.That(RenderedOptions(view),
				Is.Not.EqualTo(initialRows).AsCollection,
				"no options were rendered at all");
		});
	}

	/// <summary>What the test's own source answers with, derived from the filter so the answer names the
	/// question it was asked.</summary>
	private static IReadOnlyList<UiOption> OptionsFor(string? filter)
	{
		var name = filter ?? "none";

		return [UiOption.Of($"{name}-1", $"{name} 1"), UiOption.Of($"{name}-2", $"{name} 2")];
	}
}
