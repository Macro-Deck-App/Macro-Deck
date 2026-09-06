import { readdirSync, readFileSync } from 'node:fs';
import * as path from 'node:path';

describe('client stylesheet', () => {
  // The specs run from `out-spec/`, so the sheet is reached from the package root.
  const css = readFileSync(path.join(__dirname, '..', 'src', 'styles.css'), 'utf8');

  it('gives the document, the body and the mount point a height to fill', () => {
    const rule = css.match(/html,\s*body,\s*#app\s*\{([^}]*)\}/);

    expect(rule).not.toBeNull();
    expect(rule![1]).toContain('height: 100%');
  });

  it('stops the page itself from scrolling, so the deck owns the viewport', () => {
    const rule = css.match(/html,\s*body,\s*#app\s*\{([^}]*)\}/);

    expect(rule![1]).toContain('overflow: hidden');
  });

  it('lets no settings row wrap, which the compatibility floor cannot measure', () => {
    // Old WebKit sizes a wrapping flex container from its first line alone, so the line a control
    // wrapped onto contributed no height and was drawn over the next row (issue #829). The rows
    // shrink their label column instead, which every engine gets right.
    // Comments stripped first: the rule explains at length why it does *not* wrap.
    const rule = css.replace(/\/\*[\s\S]*?\*\//g, '').match(/\.wc-settings-row\s*\{([^}]*)\}/);

    expect(rule).not.toBeNull();
    expect(rule![1]).toContain('flex-wrap: nowrap');
  });

  it('carries no construct the compatibility floor drops', () => {
    const withoutComments = css.replace(/\/\*[\s\S]*?\*\//g, '');

    expect(/(^|[^-\w])inset\s*:/.test(withoutComments)).toBeFalse();
    expect(/#[0-9a-fA-F]{8}\b/.test(withoutComments)).toBeFalse();
  });
});

describe('bundled stylesheets', () => {
  const listed = (): string[] => {
    const source = readFileSync(path.join(__dirname, '..', 'stylesheet.mjs'), 'utf8');
    const declaration = /const RUNTIME_SHEETS = \[([\s\S]*?)\]/.exec(source);
    if (declaration === null) return [];
    return Array.from(declaration[1].matchAll(/'([\w-]+\.css)'/g)).map(match => match[1]);
  };

  it('bundles every sheet the runtime package ships', () => {
    const shipped = readdirSync(path.join(__dirname, '..', '..', 'runtime', 'styles'))
      .filter(entry => entry.endsWith('.css'));

    expect(listed().slice().sort()).toEqual(shipped.slice().sort());
  });

  it('puts the tokens first, because every other sheet reads them', () => {
    expect(listed()[0]).toBe('tokens.css');
  });
});

describe('modal chrome', () => {
  const DIALOGS = ['.wc-modal', '.wc-client-settings-dialog', '.wc-setup-dialog'];
  const BACKDROPS = ['.wc-modal-backdrop', '.wc-client-settings-backdrop', '.wc-setup-backdrop'];

  interface Rule {
    readonly media: string;
    readonly selectors: readonly string[];
    readonly body: string;
  }

  const read = (...segments: string[]): string =>
    readFileSync(path.join(__dirname, '..', 'src', ...segments), 'utf8')
      .replace(/\/\*[\s\S]*?\*\//g, '');

  const parse = (css: string, media: string, into: Rule[]): void => {
    let at = 0;
    for (;;) {
      const open = css.indexOf('{', at);
      if (open === -1) return;
      const prelude = css.slice(at, open).trim();
      let depth = 1;
      let end = open + 1;
      while (end < css.length && depth > 0) {
        if (css[end] === '{') depth += 1;
        else if (css[end] === '}') depth -= 1;
        end += 1;
      }
      const body = css.slice(open + 1, end - 1);
      if (prelude.startsWith('@media')) parse(body, prelude.slice('@media'.length).trim(), into);
      else into.push({ media, selectors: prelude.split(',').map(part => part.trim()), body });
      at = end;
    }
  };

  const sheet = (): Rule[] => {
    const rules: Rule[] = [];
    parse(read('styles.css'), '', rules);
    parse(read('setup', 'setup.css'), '', rules);
    parse(read('settings', 'settings.css'), '', rules);
    return rules;
  };

  const matches = (prelude: string, width: number, height: number): boolean =>
    prelude.split(',').some(query => {
      const features = Array.from(query.matchAll(/\((\w[\w-]*)\s*:\s*(\d+)px\)/g));
      return features.length > 0 && features.every(([, feature, value]) => {
        const size = feature.endsWith('width') ? width : height;
        return feature.startsWith('max-') ? size <= Number(value) : size >= Number(value);
      });
    });

  const resolved = (selector: string, property: string, width: number, height: number): string | null => {
    let value: string | null = null;
    for (const rule of sheet()) {
      if (!rule.selectors.includes(selector)) continue;
      if (rule.media !== '' && !matches(rule.media, width, height)) continue;
      const found = Array.from(rule.body.matchAll(/([\w-]+)\s*:\s*([^;]+);/g))
        .filter(([, name]) => name === property);
      if (found.length > 0) value = found[found.length - 1][2].trim();
    }
    return value;
  };

  // A desktop display, where the modals keep the existing centred behaviour.
  const DESKTOP: [number, number] = [1280, 800];

  it('opens every dialog on the same surface', () => {
    // The design system's dialog surface, which the desktop UI's modal already uses: the same dialog
    // opens in both apps and the two side by side must not be two different dialogs. The settings and
    // setup dialogs used to draw themselves on a bordered `--color-bg-secondary` card instead.
    for (const dialog of DIALOGS) {
      expect(resolved(dialog, 'background', ...DESKTOP)).toBe('var(--color-bg-elevated)');
      expect(resolved(dialog, 'border-radius', ...DESKTOP)).toBe('var(--radius-xl)');
      expect(resolved(dialog, 'box-shadow', ...DESKTOP)).toBe('var(--shadow-dialog)');
      expect(resolved(dialog, 'border', ...DESKTOP)).toBeNull();
    }
  });

  it('draws every backdrop with the scrim token, not a colour of its own', () => {
    for (const backdrop of BACKDROPS) {
      expect(resolved(backdrop, 'background', ...DESKTOP)).toBe('var(--color-scrim)');
    }
  });

  it('fills the display on a 7-inch deck', () => {
    // 1024x600 and 800x480 are the common 7-inch panels; the second is also the Car Thing. Neither is
    // narrow enough for a width-only rule to catch, which is why the condition is width *or* height.
    for (const [width, height] of [[1024, 600], [800, 480]] as [number, number][]) {
      for (const dialog of DIALOGS) {
        expect(resolved(dialog, 'max-width', width, height)).toBe('none');
        expect(resolved(dialog, 'max-height', width, height)).toBe('none');
        expect(resolved(dialog, 'height', width, height)).toBe('100%');
        expect(resolved(dialog, 'border-radius', width, height)).toBe('0');
      }
      for (const backdrop of BACKDROPS) {
        // The dialog's `height: 100%` needs a parent with a definite height to resolve against.
        expect(resolved(backdrop, 'height', width, height)).toBe('100%');
        expect(resolved(backdrop, 'padding', width, height)).toBe('0');
      }
    }
  });

  it('leaves the centred dialog alone on a larger display', () => {
    for (const dialog of DIALOGS) {
      expect(resolved(dialog, 'max-width', ...DESKTOP)).toBe('34rem');
      expect(resolved(dialog, 'border-radius', ...DESKTOP)).toBe('var(--radius-xl)');
    }
  });

  it('keeps the full-screen sheet clear of the hardware', () => {
    // Only the full-screen sheet reaches the display edges, and the backdrop is fixed, so it escapes
    // the insets `.wc-root` already pads for. The `0px` fallback is load bearing: an engine that does
    // not know `env()` drops the whole declaration, and the header would lose its padding entirely.
    for (const side of ['right', 'bottom', 'left']) {
      expect(resolved('.wc-modal', `padding-${side}`, 800, 480))
        .toBe(`env(safe-area-inset-${side}, 0px)`);
    }
    expect(resolved('.wc-modal-header', 'padding-top', 800, 480))
      .toBe('calc(1rem + env(safe-area-inset-top, 0px))');
  });
});
