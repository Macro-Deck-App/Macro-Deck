'use strict';

(() => {
  const POLL_MS = 500;
  const RELEASE_REPO = 'Macro-Deck-App/Macro-Deck';
  const WHATS_CHANGED = /^what['’]s changed$/i;
  const GITHUB_REFERENCE = /^https:\/\/github\.com\/([\w.-]+\/[\w.-]+)\/(?:pull|issues)\/(\d+)\/?$/;
  const GITHUB_COMPARE = /^https:\/\/github\.com\/[\w.-]+\/[\w.-]+\/compare\/([^/?#]+)$/;
  const INLINE = /\[([^\]]+)\]\((https:\/\/[^\s)]+)\)|(https:\/\/[^\s<>()]*[^\s<>().,;:!?'"])|\*\*([^*]+)\*\*|`([^`]+)`|(^|[^\w@/.])@([A-Za-z0-9][A-Za-z0-9-]{0,38})/g;

  function parseBlocks(text) {
    const blocks = [];
    let paragraph = null;
    let list = null;
    const flush = () => {
      paragraph = null;
      list = null;
    };
    for (const raw of text.replace(/<!--[\s\S]*?(?:-->|$)/g, '').replace(/\r\n?/g, '\n').split('\n')) {
      const line = raw.trimEnd();
      const heading = /^(#{1,6})\s+(.*)$/.exec(line);
      const item = /^\s*[-*+]\s+(.*)$/.exec(line);
      if (line.trim() === '') {
        flush();
      } else if (heading) {
        flush();
        blocks.push({ kind: 'heading', text: heading[2].replace(/\s+#+$/, '') });
      } else if (item) {
        paragraph = null;
        if (!list) {
          list = { kind: 'list', items: [] };
          blocks.push(list);
        }
        list.items.push(item[1]);
      } else if (list && /^\s/.test(raw)) {
        list.items[list.items.length - 1] += ` ${line.trim()}`;
      } else if (paragraph) {
        paragraph.text += ` ${line.trim()}`;
      } else {
        list = null;
        paragraph = { kind: 'paragraph', text: line.trim() };
        blocks.push(paragraph);
      }
    }
    return blocks;
  }

  function sections(text) {
    const blocks = parseBlocks(text);
    if (blocks[0]?.kind === 'heading' && WHATS_CHANGED.test(blocks[0].text.trim())) {
      blocks.shift();
    }
    const result = [];
    let current = null;
    for (const block of blocks) {
      const closesCategory = current !== null
        && current[0].kind === 'heading'
        && current[current.length - 1].kind === 'list'
        && block.kind !== 'list';
      if (current === null || block.kind === 'heading' || closesCategory) {
        current = [];
        result.push(current);
      }
      current.push(block);
    }
    return result;
  }

  function shortLabel(href) {
    const reference = GITHUB_REFERENCE.exec(href);
    if (reference) {
      return reference[1] === RELEASE_REPO ? `#${reference[2]}` : `${reference[1]}#${reference[2]}`;
    }
    return GITHUB_COMPARE.exec(href)?.[1] ?? href;
  }

  function appendInline(parent, text) {
    let last = 0;
    for (const match of text.matchAll(INLINE)) {
      const [whole, label, href, bare, bold, code, mentionPrefix, mention] = match;
      let start = match.index;
      if (mention !== undefined) {
        start += mentionPrefix.length;
      }
      if (start > last) {
        parent.append(text.slice(last, start));
      }
      if (label !== undefined) {
        parent.append(link(href, label));
      } else if (bare !== undefined) {
        parent.append(link(bare, shortLabel(bare)));
      } else if (bold !== undefined) {
        const strong = document.createElement('strong');
        strong.textContent = bold;
        parent.append(strong);
      } else if (code !== undefined) {
        const element = document.createElement('code');
        element.textContent = code;
        parent.append(element);
      } else {
        parent.append(link(`https://github.com/${mention}`, `@${mention}`));
      }
      last = match.index + whole.length;
    }
    if (last < text.length) {
      parent.append(text.slice(last));
    }
  }

  function link(href, text) {
    const anchor = document.createElement('a');
    anchor.href = href;
    anchor.textContent = text;
    return anchor;
  }

  function renderChangelog(container, view) {
    container.replaceChildren();
    const notes = view.notes ? sections(view.notes) : [];
    if (notes.length === 0) {
      const empty = document.createElement('p');
      empty.className = 'update__muted';
      if (view.notesUrl?.startsWith('https://')) {
        empty.append(link(view.notesUrl, view.notesLink));
      } else {
        empty.textContent = view.noChangelog;
      }
      container.append(empty);
      return;
    }
    for (const section of notes) {
      const element = document.createElement('div');
      element.className = 'update__notes-section';
      if (section[0].kind === 'heading') {
        element.classList.add('update__notes-section--category');
      }
      for (const block of section) {
        if (block.kind === 'list') {
          const list = document.createElement('ul');
          for (const item of block.items) {
            const entry = document.createElement('li');
            appendInline(entry, item);
            list.append(entry);
          }
          element.append(list);
        } else {
          const node = document.createElement(block.kind === 'heading' ? 'h3' : 'p');
          appendInline(node, block.text);
          element.append(node);
        }
      }
      container.append(element);
    }
  }

  function renderActions(container, actions) {
    container.replaceChildren();
    for (const action of actions) {
      const button = document.createElement('button');
      button.type = 'button';
      button.className = action.primary ? 'update__button update__button--primary' : 'update__button';
      button.textContent = action.label;
      button.dataset.action = action.id;
      button.addEventListener('click', () => {
        for (const other of container.querySelectorAll('button')) {
          other.disabled = true;
        }
        void post(action.id);
      });
      container.append(button);
    }
  }

  function render(view, previous) {
    const root = document.documentElement;
    root.lang = view.lang ?? '';
    root.classList.toggle('light', view.theme === 'light');
    root.classList.toggle('dark', view.theme === 'dark');
    if (/^#[0-9a-f]{6}$/i.test(view.accent ?? '')) {
      root.style.setProperty('--color-accent', view.accent);
    }
    document.title = view.title;
    document.getElementById('heading').textContent = view.heading ?? '';
    document.getElementById('current').textContent = view.currentVersion;
    document.getElementById('changelog-heading').textContent = view.changelogHeading;
    if (
      previous?.notes !== view.notes
      || previous?.notesUrl !== view.notesUrl
      || previous?.noChangelog !== view.noChangelog
    ) {
      renderChangelog(document.getElementById('changelog'), view);
    }

    const status = document.getElementById('status');
    status.hidden = !view.status;
    status.textContent = view.status?.text ?? '';
    status.classList.toggle('update__status--error', view.status?.kind === 'error');
    status.setAttribute('aria-live', view.downloading ? 'off' : 'polite');

    document.getElementById('progress').hidden = !view.downloading;
    const bar = document.getElementById('bar');
    bar.setAttribute('aria-valuetext', view.downloading ? view.status?.text ?? '' : '');
    if (view.progressPercent === null || view.progressPercent === undefined) {
      bar.removeAttribute('value');
    } else {
      bar.value = view.progressPercent;
    }

    if (JSON.stringify(previous?.actions) !== JSON.stringify(view.actions)) {
      renderActions(document.getElementById('actions'), view.actions);
    }
  }

  const actionPaths = {
    later: 'actions/later',
    install: 'actions/install',
    cancel: 'actions/cancel',
    downloadPage: 'actions/download-page',
  };

  async function post(id) {
    try {
      await fetch(actionPaths[id], { method: 'POST' });
    } catch {
      return;
    }
    await refresh();
    for (const button of document.querySelectorAll('#actions button')) {
      button.disabled = false;
    }
  }

  let current = null;

  async function refresh() {
    try {
      const response = await fetch('state', { cache: 'no-store' });
      if (!response.ok) {
        return;
      }
      const view = await response.json();
      if (JSON.stringify(view) !== JSON.stringify(current)) {
        render(view, current);
        current = view;
      }
    } catch {
      return;
    }
  }

  async function poll() {
    await refresh();
    setTimeout(poll, POLL_MS);
  }

  void poll();
})();
