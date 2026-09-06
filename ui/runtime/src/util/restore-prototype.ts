// Required by the ES5 target: Error.call(this) returns a new object instead of initialising this,
// so a downlevelled class X extends Error fails every instanceof X. Call right after super().
export function restorePrototype(instance: object, prototype: object): void {
  if (typeof Object.setPrototypeOf === 'function') {
    Object.setPrototypeOf(instance, prototype);
    return;
  }
  (instance as { __proto__?: object }).__proto__ = prototype;
}
