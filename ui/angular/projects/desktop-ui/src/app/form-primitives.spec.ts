describe('form primitives', () => {
  let host: HTMLElement;

  beforeEach(() => {
    host = document.createElement('div');
    host.style.position = 'absolute';
    document.body.appendChild(host);
  });

  afterEach(() => host.remove());

  function render(markup: string): (selector: string) => CSSStyleDeclaration {
    host.innerHTML = markup;
    return selector => getComputedStyle(host.querySelector(selector)!);
  }

  function resolvedColor(token: string): string {
    const probe = document.createElement('span');
    probe.style.color = `var(${token})`;
    host.appendChild(probe);
    const color = getComputedStyle(probe).color;
    probe.remove();
    return color;
  }

  const group = (modifier = ''): string => `
    <div class="form-group ${modifier}">
      <label>Real label</label>
      <span class="form-label">Wrapped label</span>
    </div>`;

  it('stacks a group and separates its rows by the base gap', () => {
    const style = render(group())('.form-group');

    expect(style.display).toBe('flex');
    expect(style.flexDirection).toBe('column');
    expect(style.rowGap).toBe('6px');
  });

  it('treats a <label> child and a .form-label span identically', () => {
    const at = render(group());
    const label = at('label');
    const span = at('.form-label');

    expect(label.fontSize).toBe('12px');
    expect(label.fontWeight).toBe('500');
    expect(label.color).toBe(resolvedColor('--color-text-secondary'));

    expect(span.fontSize).toBe(label.fontSize);
    expect(span.fontWeight).toBe(label.fontWeight);
    expect(span.color).toBe(label.color);
  });

  it('tightens the gap and the label in the dense variant', () => {
    const at = render(group('form-group--dense'));

    expect(at('.form-group').rowGap).toBe('3px');
    expect(at('label').fontSize).toBe('12px');
    expect(at('.form-label').fontSize).toBe('12px');
    expect(at('label').fontWeight).toBe('500');
  });

  it('lets a dense group shrink below its content', () => {
    expect(render(group('form-group--dense'))('.form-group').minWidth).toBe('0px');
  });

  it('renders the section variant label as an uppercase mini-heading', () => {
    const at = render(group('form-group--section'));

    expect(at('label').textTransform).toBe('uppercase');
    expect(at('label').fontSize).toBe('12px');
    expect(at('label').fontWeight).toBe('600');
    expect(at('label').color).toBe(resolvedColor('--color-text-muted'));
  });

  it('spreads a section label across the row so the value sits opposite it', () => {
    const style = render(group('form-group--section'))('label');

    expect(style.display).toBe('flex');
    expect(style.justifyContent).toBe('space-between');
  });
});
