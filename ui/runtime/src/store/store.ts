export interface ReadableStore<T> {
  get(): T;
  subscribe(listener: (value: T) => void): () => void;
}

export interface WritableStore<T> extends ReadableStore<T> {
  set(value: T): void;
  update(change: (value: T) => T): void;
}

export function store<T>(initial: T): WritableStore<T> {
  let value = initial;
  let listeners: Array<(next: T) => void> = [];

  return {
    get(): T {
      return value;
    },

    set(next: T): void {
      if (next === value) return;
      value = next;
      const notified = listeners.slice();
      for (let index = 0; index < notified.length; index++) notified[index](value);
    },

    update(change: (current: T) => T): void {
      this.set(change(value));
    },

    subscribe(listener: (next: T) => void): () => void {
      listeners.push(listener);
      let live = true;
      return () => {
        if (!live) return;
        live = false;
        const at = listeners.indexOf(listener);
        if (at >= 0) listeners = listeners.slice(0, at).concat(listeners.slice(at + 1));
      };
    },
  };
}
