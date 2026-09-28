import {
  OUTDATED_UI_ID,
  OutdatedUiOptions,
  OutdatedUiText,
  OutdatedUiVariant,
  currentOutdatedUi,
  diagnoseOutdatedUi,
  readUiCommitFromHtml,
  showOutdatedUi,
} from './outdated-ui';

function textFor(language: string): (variant: OutdatedUiVariant) => OutdatedUiText {
  return variant => ({
    title: `${language} ${variant} title`,
    body: `${language} body`,
    steps: [`${language} step 1`, `${language} step 2`],
    action: `${language} action`,
    versions: `${language} versions`,
  });
}

function pageWithDeck(): { doc: Document; deck: HTMLElement } {
  const doc = document.implementation.createHTMLDocument();
  const deck = doc.createElement('main');
  doc.body.appendChild(deck);
  return { doc, deck };
}

function overlayOf(doc: Document): HTMLElement {
  const overlay = doc.getElementById(OUTDATED_UI_ID);
  if (!overlay) throw new Error('no overlay');
  return overlay;
}

describe('outdated UI screen', () => {
  it('explains the problem with an icon, steps and an action, and covers the page', () => {
    const { doc, deck } = pageWithDeck();
    const onAction = jasmine.createSpy('onAction');

    showOutdatedUi(doc, { variant: 'device', text: textFor('en'), onAction });

    const overlay = overlayOf(doc);
    expect(overlay.getAttribute('role')).toBe('alert');
    expect(overlay.querySelector('svg')).not.toBeNull();
    expect(overlay.querySelector('h1')!.textContent).toBe('en device title');
    expect(Array.from(overlay.querySelectorAll('li')).map(item => item.textContent))
      .toEqual(['en step 1', 'en step 2']);
    expect(overlay.textContent).toContain('en versions');
    expect(deck.inert).toBeTrue();

    overlay.querySelector('button')!.click();
    expect(onAction).toHaveBeenCalledTimes(1);
  });

  it('keeps a plain value in front of every theme token so old engines still paint it', () => {
    const { doc } = pageWithDeck();
    showOutdatedUi(doc, { variant: 'installation', text: textFor('en'), onAction: () => undefined });

    const styled = [overlayOf(doc)].concat(Array.from(overlayOf(doc).querySelectorAll<HTMLElement>('[style]')));
    for (const element of styled) {
      const declarations = element.getAttribute('style')!.split(';');
      declarations.forEach((declaration, index) => {
        if (declaration.indexOf('var(') < 0) return;
        const property = declaration.split(':')[0];
        const plain = declarations.slice(0, index)
          .some(earlier => earlier.split(':')[0] === property && earlier.indexOf('var(') < 0);
        expect(plain).withContext(`${element.tagName} ${declaration}`).toBeTrue();
      });
    }
  });

  it('follows a language that arrives after it was shown, and stops once removed', () => {
    const { doc } = pageWithDeck();
    let language = 'en';
    const listeners: Array<() => void> = [];
    const options: OutdatedUiOptions = {
      variant: 'device',
      text: variant => textFor(language)(variant),
      onAction: () => undefined,
      onTextChange: listener => {
        listeners.push(listener);
        return () => listeners.splice(listeners.indexOf(listener), 1);
      },
    };

    const handle = showOutdatedUi(doc, options);
    language = 'de';
    listeners.forEach(listener => listener());
    expect(overlayOf(doc).querySelector('h1')!.textContent).toBe('de device title');

    handle.remove();
    expect(listeners.length).toBe(0);
  });

  it('switches to another diagnosis in place instead of stacking a second screen', () => {
    const { doc } = pageWithDeck();
    showOutdatedUi(doc, { variant: 'device', text: textFor('en'), onAction: () => undefined });
    showOutdatedUi(doc, { variant: 'installation', text: textFor('en'), onAction: () => undefined });

    expect(doc.querySelectorAll('#' + OUTDATED_UI_ID).length).toBe(1);
    expect(currentOutdatedUi(doc)!.variant).toBe('installation');
    expect(overlayOf(doc).querySelector('h1')!.textContent).toBe('en installation title');
    currentOutdatedUi(doc)!.remove();
  });

  it('gives the page back as it was once removed', () => {
    const { doc, deck } = pageWithDeck();
    const alreadyInert = doc.createElement('aside');
    alreadyInert.inert = true;
    doc.body.appendChild(alreadyInert);

    showOutdatedUi(doc, { variant: 'device', text: textFor('en'), onAction: () => undefined }).remove();

    expect(doc.getElementById(OUTDATED_UI_ID)).toBeNull();
    expect(currentOutdatedUi(doc)).toBeNull();
    expect(deck.inert).toBeFalse();
    expect(alreadyInert.inert).toBeTrue();
  });
});

describe('outdated UI diagnosis', () => {
  it('blames this device when the host serves the build it reports', () => {
    expect(diagnoseOutdatedUi('abc1234', 'ABC1234')).toBe('device');
  });

  it('blames the installation when the host serves a different build than it reports', () => {
    expect(diagnoseOutdatedUi('abc1234', 'fff0000')).toBe('installation');
  });

  it('falls back to this device when the served build cannot be read', () => {
    expect(diagnoseOutdatedUi('abc1234', null)).toBe('device');
  });

  it('reads the build a served page was made from', () => {
    const html = '<head>\n  <meta name="macro-deck-ui-commit" content=" abc1234 ">\n</head>';
    expect(readUiCommitFromHtml(html)).toBe('abc1234');
    expect(readUiCommitFromHtml('<head></head>')).toBeNull();
  });
});
