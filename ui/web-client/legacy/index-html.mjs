// The one HTML document the build produces, as pure string transforms so build-legacy.mjs stays
// orchestration-only and this logic is unit-testable.
//
// There is a single shell and a single bundle: every browser runs the same down-levelled ES5
// client (issue #829). What still varies by engine is the stylesheet, because the sheets are
// authored with custom properties and the floor engines have none - so the shell carries a small
// ES5 probe that pulls in the resolved stylesheet only where `var()` would be dropped.

/** Marks the probe in the emitted shell, so a test can find it and a second build can refuse. */
export const STYLE_PROBE_MARKER = 'data-legacy-styles';

/** Holds the accent rules `appearance.ts` fills in; inert, never parsed as CSS or as script. */
export const ACCENT_TEMPLATE_ID = 'md-accent-template';

/**
 * Appended when the engine has no custom properties, ahead of first paint.
 *
 * `document.write` rather than appending a <link>: this runs while the head is still parsing, so
 * the sheet blocks rendering the way a declared <link> would. Appending would paint the page with
 * every var()-bearing declaration dropped first, which is the flash of unstyled deck this exists
 * to prevent. The engines that take this branch are the ones that support document.write best.
 */
export function styleProbeScript(legacyStylesHref) {
  return `<script ${STYLE_PROBE_MARKER}>(function(){`
    + 'var s=typeof CSS!=="undefined"&&!!CSS.supports&&CSS.supports("--md-probe","0");'
    + `if(!s){document.write('<link rel="stylesheet" href="${legacyStylesHref}">');}`
    + '})();</script>';
}

/**
 * The shipped shell: the modern bundle swapped for the polyfills + down-levelled pair, the
 * stylesheet probe injected, and the accent rules carried along for the engines that need them.
 */
export function buildShell(html, { polyfillsSrc, mainSrc, legacyStylesHref, accentTemplate }) {
  if (html.includes(STYLE_PROBE_MARKER)) {
    throw new Error('index.html already carries the stylesheet probe; expected a fresh build output.');
  }
  if (!html.includes('</body>')) {
    throw new Error('index.html has no </body> element.');
  }
  if (!html.includes('</head>')) {
    throw new Error('index.html has no </head> element.');
  }
  // The modern bundle's name is content-hashed, so it is matched by shape rather than by a name
  // this module could know: whatever `main-<hash>.js` the shell loads is the script being replaced.
  const modernScript = /\s*<script[^>]*\ssrc="main-[A-Za-z0-9]+\.js"[^>]*><\/script>/;
  if (!modernScript.test(html)) {
    throw new Error('index.html loads no main bundle.');
  }
  let result = html.replace(modernScript, '');
  const scripts = `<script src="${polyfillsSrc}"></script>\n<script src="${mainSrc}"></script>\n`;
  result = result.replace('</body>', scripts + '</body>');

  // Last thing in the head, so the resolved sheet lands after the authored one and wins wherever
  // both parse. Placed earlier it would be the authored sheet that had the final say, and every
  // declaration this pass rewrote to survive an old parser would be overridden by the version that
  // does not.
  result = result.replace('</head>', `${styleProbeScript(legacyStylesHref)}\n</head>`);
  if (accentTemplate) {
    result = result.replace('</body>',
      `<script type="text/plain" id="${ACCENT_TEMPLATE_ID}">\n${accentTemplate}</script>\n</body>`);
  }
  return result;
}
