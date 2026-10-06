export class MotionPreference {
  private reducedMotion = false;
  private readonly listeners: (() => void)[] = [];

  constructor() {
    if (typeof window.matchMedia !== 'function') return;
    const query = window.matchMedia('(prefers-reduced-motion: reduce)');
    this.reducedMotion = query.matches;

    const onChange = () => {
      if (query.matches === this.reducedMotion) return;
      this.reducedMotion = query.matches;
      for (let index = 0; index < this.listeners.length; index++) this.listeners[index]();
    };

    // addListener is the only one Safari before 14 has, which the compatibility floor still covers.
    if (typeof query.addEventListener === 'function') query.addEventListener('change', onChange);
    else if (typeof query.addListener === 'function') query.addListener(onChange);
  }

  reduced(): boolean {
    return this.reducedMotion;
  }

  onChange(listener: () => void): () => void {
    this.listeners.push(listener);
    return () => {
      const at = this.listeners.indexOf(listener);
      if (at >= 0) this.listeners.splice(at, 1);
    };
  }
}
