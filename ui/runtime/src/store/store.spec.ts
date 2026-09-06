import { store } from './store';

describe('store', () => {
  it('holds the value it was given', () => {
    expect(store(7).get()).toBe(7);
  });

  it('tells every subscriber about a change', () => {
    const seen: number[][] = [[], []];
    const counter = store(0);
    counter.subscribe(value => seen[0].push(value));
    counter.subscribe(value => seen[1].push(value));

    counter.set(1);
    counter.set(2);

    expect(seen).toEqual([[1, 2], [1, 2]]);
  });

  it('says nothing when set to the value it already holds', () => {
    // A poll that answers the same thing twice must not repaint a deck.
    const seen: string[] = [];
    const state = store('connected');
    state.subscribe(value => seen.push(value));

    state.set('connected');

    expect(seen).toEqual([]);
  });

  it('derives the next value from the current one', () => {
    const counter = store(2);

    counter.update(value => value * 5);

    expect(counter.get()).toBe(10);
  });

  it('stops notifying an unsubscribed listener', () => {
    const seen: number[] = [];
    const counter = store(0);
    const stop = counter.subscribe(value => seen.push(value));

    counter.set(1);
    stop();
    counter.set(2);

    expect(seen).toEqual([1]);
  });

  it('ignores a second unsubscribe instead of dropping someone else', () => {
    const seen: number[] = [];
    const counter = store(0);
    const stop = counter.subscribe(() => undefined);
    counter.subscribe(value => seen.push(value));

    stop();
    stop();
    counter.set(1);

    expect(seen).toEqual([1]);
  });

  it('still notifies the remaining listeners when one unsubscribes mid-notification', () => {
    // A listener that tears itself down on the change it was waiting for is ordinary; it must not
    // cost the listener after it the same notification.
    const seen: number[] = [];
    const counter = store(0);
    const stop = counter.subscribe(() => stop());
    counter.subscribe(value => seen.push(value));

    counter.set(1);

    expect(seen).toEqual([1]);
  });
});
