import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  Output,
  ViewChild,
  computed,
  effect,
  inject,
  signal,
  untracked,
} from '@angular/core';
import { AppStrings, VariableTokenKind, VariableTokenTrigger, findVariableTokenTrigger, hasVariableToken, matchesVariableTokenQuery, parseVariableSegments, variableTokenKind, variableTokenLabel, variableTokenText } from '@macro-deck/runtime';
import { LocalizationService, OverlayPanelComponent } from '@shared';
import type { Variable, VariableType } from '@macro-deck/runtime';

export interface VariableSuggestion {
  kind: VariableTokenKind;
  name: string;
  label: string;
  token: string;
  meta: string;
}

interface TextRange {
  start: number;
  end: number;
}

const MAX_SUGGESTIONS = 50;

@Component({
  selector: 'shared-variable-text-input',
  standalone: true,
  imports: [OverlayPanelComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div
      #editor
      class="vti-editor"
      [class.vti-multiline]="multiline"
      [class.vti-empty]="valueState().length === 0"
      [class.vti-invalid]="invalid"
      [class.vti-disabled]="disabled"
      [attr.contenteditable]="disabled ? 'false' : 'true'"
      [attr.data-placeholder]="placeholder"
      [attr.aria-label]="ariaLabel || null"
      [attr.aria-multiline]="multiline"
      [attr.aria-expanded]="isOpen()"
      [style.--vti-rows]="multiline ? rows : null"
      role="textbox"
      aria-autocomplete="list"
      spellcheck="false"
      (input)="onEditorInput()"
      (keydown)="onKeyDown($event)"
      (keyup)="onCaretMoved()"
      (mousedown)="onEditorMouseDown($event)"
      (mouseup)="onCaretMoved()"
      (paste)="onPaste($event)"
      (drop)="$event.preventDefault()"
      (blur)="onBlur()"
      (compositionstart)="composing = true"
      (compositionend)="onCompositionEnd()"></div>

    <shared-overlay-panel
      [x]="caretPoint().x"
      [y]="caretPoint().y"
      [isOpen]="isOpen()"
      [minWidth]="240"
      [maxHeight]="220"
      (dismissed)="dismissSuggestions()">
      <div class="vti-options" role="listbox">
        @for (suggestion of suggestions(); track suggestion.token; let index = $index) {
          <div
            class="vti-option"
            [class.vti-option-active]="index === activeIndex()"
            role="option"
            [attr.aria-selected]="index === activeIndex()"
            (mousedown)="$event.preventDefault()"
            (click)="accept(suggestion)">
            <span class="vti-option-label">{{ suggestion.label }}</span>
            @if (suggestion.meta) {
              <span class="vti-option-meta">{{ suggestion.meta }}</span>
            }
          </div>
        }
      </div>
    </shared-overlay-panel>
  `,
  styleUrls: ['./variable-text-input.component.scss'],
})
export class VariableTextInputComponent implements AfterViewInit {
  @Input() set value(value: string) {
    this.valueState.set(value ?? '');
  }
  get value(): string {
    return this.valueState();
  }

  @Input() set variables(variables: Variable[]) {
    this.variablesState.set(variables ?? []);
  }
  get variables(): Variable[] {
    return this.variablesState();
  }

  @Input() set acceptedTypes(types: VariableType[] | undefined | null) {
    this.acceptedTypesState.set(types && types.length > 0 ? types : null);
  }

  @Input() placeholder = '';
  @Input() multiline = false;
  @Input() rows = 4;
  @Input() disabled = false;
  @Input() invalid = false;
  @Input() ariaLabel = '';

  @Output() valueChange = new EventEmitter<string>();
  @Output() touched = new EventEmitter<void>();

  @ViewChild('editor') private editorRef?: ElementRef<HTMLElement>;

  readonly valueState = signal('');
  readonly isOpen = signal(false);
  readonly suggestions = signal<VariableSuggestion[]>([]);
  readonly activeIndex = signal(0);
  readonly caretPoint = signal<{ x: number; y: number }>({ x: 0, y: 0 });

  composing = false;

  private readonly variablesState = signal<Variable[]>([]);
  private readonly acceptedTypesState = signal<VariableType[] | null>(null);
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly localization = inject(LocalizationService);

  private readonly knownNames = computed(() => {
    const names = new Set<string>();
    for (const variable of this.variablesState()) {
      names.add(variableTokenLabel(variableTokenKind(variable), variable.name));
    }
    return names;
  }, { equal: sameNames });

  private renderedValue: string | null = null;
  private renderedNames: ReadonlySet<string> | null = null;
  private lastSelection: TextRange | null = null;
  private trigger: VariableTokenTrigger | null = null;
  private dismissed: { start: number; query: string } | null = null;

  private static readonly BLOCK_TAGS = new Set(['DIV', 'P', 'LI', 'BLOCKQUOTE', 'PRE']);
  private static readonly CHIP_MASK = '\u0000';
  private static readonly CARET_ANCHOR = '\u200B';

  constructor() {
    effect(() => {
      const value = this.valueState();
      const names = this.knownNames();
      if (!this.editorRef) return;
      untracked(() => {
        if (value !== this.renderedValue) {
          this.renderPreservingCaret();
        } else if (names !== this.renderedNames) {
          this.refreshChipStates();
        }
      });
    });
  }

  ngAfterViewInit(): void {
    this.render();
  }

  insertToken(token: string): void {
    const value = this.valueState();
    const selection = this.currentSelection();
    this.commit(value.slice(0, selection.start) + token + value.slice(selection.end),
      selection.start + token.length);
  }

  focus(): void {
    this.editorRef?.nativeElement.focus();
  }

  onEditorInput(): void {
    if (this.composing) return;
    const editor = this.editorRef?.nativeElement;
    if (!editor) return;

    const next = this.serialize(editor);
    const selection = this.selectionOffsets();
    this.valueState.set(next);
    this.renderedValue = next;

    if (this.hasLooseToken(editor)) {
      this.render();
      if (selection) this.setCaret(selection.end);
    }

    this.rememberSelection();
    this.valueChange.emit(next);
    this.syncSuggestions();
  }

  onCompositionEnd(): void {
    this.composing = false;
    this.onEditorInput();
  }

  onCaretMoved(): void {
    this.rememberSelection();
    this.syncSuggestions();
  }

  onKeyDown(event: KeyboardEvent): void {
    if (this.isOpen() && this.suggestions().length > 0) {
      switch (event.key) {
        case 'ArrowDown':
          event.preventDefault();
          this.moveActive(1);
          return;
        case 'ArrowUp':
          event.preventDefault();
          this.moveActive(-1);
          return;
        case 'Enter':
        case 'Tab': {
          const suggestion = this.suggestions()[this.activeIndex()];
          if (suggestion) {
            event.preventDefault();
            this.accept(suggestion);
          }
          return;
        }
        case 'Escape':
          event.preventDefault();
          event.stopPropagation();
          this.dismissSuggestions();
          return;
        default:
          break;
      }
    }

    if (event.key === 'ArrowLeft' || event.key === 'ArrowRight') {
      this.moveCaret(event, event.key === 'ArrowRight' ? 1 : -1);
      return;
    }

    if (event.key !== 'Enter') return;
    event.preventDefault();
    if (!this.multiline) return;
    const value = this.valueState();
    const selection = this.currentSelection();
    this.commit(value.slice(0, selection.start) + '\n' + value.slice(selection.end),
      selection.start + 1);
  }

  onPaste(event: ClipboardEvent): void {
    event.preventDefault();
    const text = event.clipboardData?.getData('text/plain') ?? '';
    if (!text || this.disabled) return;
    const insert = this.multiline ? text : text.replace(/[\r\n]+/g, ' ');
    const value = this.valueState();
    const selection = this.currentSelection();
    this.commit(value.slice(0, selection.start) + insert + value.slice(selection.end),
      selection.start + insert.length);
  }

  onEditorMouseDown(event: MouseEvent): void {
    const remove = (event.target as HTMLElement | null)?.closest<HTMLElement>('.vti-chip-remove');
    const chip = remove?.parentElement;
    const token = chip?.dataset['token'];
    if (!chip || token === undefined || this.disabled) return;
    if (this.isUnderOverlaidAction(event)) return;

    const start = this.offsetBefore(chip);
    if (start === null) return;
    event.preventDefault();
    const value = this.valueState();
    this.commit(value.slice(0, start) + value.slice(start + token.length), start);
  }

  // An action overlaid on the field hides whatever scrolled under the band it reserved, and the
  // backdrop passes clicks through so the field stays focusable there.
  private isUnderOverlaidAction(event: MouseEvent): boolean {
    const editor = this.editorRef?.nativeElement;
    if (!editor) return false;

    const style = getComputedStyle(editor);
    const band = parseFloat(style.scrollPaddingInlineEnd);
    if (!Number.isFinite(band)) return false;

    const contentEdge = editor.getBoundingClientRect().right
      - band - parseFloat(style.borderInlineEndWidth);
    return event.clientX > contentEdge;
  }

  onBlur(): void {
    this.closeSuggestions();
    this.reconcile();
    this.touched.emit();
  }

  private reconcile(): void {
    const editor = this.editorRef?.nativeElement;
    if (!editor) return;
    if (this.serialize(editor) !== this.valueState() || this.hasLooseToken(editor)) {
      this.render();
    }
  }

  accept(suggestion: VariableSuggestion): void {
    const value = this.valueState();
    const selection = this.currentSelection();
    const start = this.trigger ? this.trigger.start : selection.start;
    this.closeSuggestions();
    this.commit(value.slice(0, start) + suggestion.token + value.slice(selection.end),
      start + suggestion.token.length);
  }

  private moveCaret(event: KeyboardEvent, delta: 1 | -1): void {
    if (event.shiftKey || event.altKey || event.ctrlKey || event.metaKey) return;
    const selection = this.selectionOffsets();
    if (!selection || selection.start !== selection.end) return;

    const target = this.steppedCaret(selection.start, delta);
    if (target === null) return;
    event.preventDefault();
    this.setCaret(target);
    this.rememberSelection();
    this.syncSuggestions();
  }

  private steppedCaret(offset: number, delta: 1 | -1): number | null {
    const value = this.valueState();
    if (delta > 0 ? offset >= value.length : offset <= 0) return null;

    let index = 0;
    for (const segment of parseVariableSegments(value)) {
      const length = segment.kind === 'text' ? segment.text.length : segment.raw.length;
      if (segment.kind !== 'text') {
        if (delta > 0 && index === offset) return index + length;
        if (delta < 0 && index + length === offset) return index;
      }
      index += length;
    }
    return offset + delta * codePointWidth(value, offset, delta);
  }

  dismissSuggestions(): void {
    if (this.trigger) {
      this.dismissed = { start: this.trigger.start, query: this.trigger.query };
    }
    this.closeSuggestions();
  }

  private closeSuggestions(): void {
    this.trigger = null;
    this.isOpen.set(false);
    this.suggestions.set([]);
  }

  private moveActive(delta: number): void {
    const count = this.suggestions().length;
    if (count === 0) return;
    this.activeIndex.set((((this.activeIndex() + delta) % count) + count) % count);
    requestAnimationFrame(() =>
      this.host.nativeElement
        .querySelector('.vti-option-active')
        ?.scrollIntoView({ block: 'nearest' }));
  }

  private commit(next: string, caret: number): void {
    this.valueState.set(next);
    this.render();
    this.focus();
    this.setCaret(caret);
    this.rememberSelection();
    this.valueChange.emit(next);
    this.syncSuggestions();
  }

  private render(): void {
    const editor = this.editorRef?.nativeElement;
    if (!editor) return;
    const value = this.valueState();
    editor.replaceChildren(...this.buildNodes(value));
    this.renderedValue = value;
    this.renderedNames = this.knownNames();
  }

  private renderPreservingCaret(): void {
    const editor = this.editorRef?.nativeElement;
    const selection = editor === document.activeElement ? this.selectionOffsets() : null;
    this.render();
    if (selection) this.setCaret(Math.min(selection.end, this.valueState().length));
  }

  private refreshChipStates(): void {
    const editor = this.editorRef?.nativeElement;
    if (!editor) return;
    const known = this.knownNames();

    for (const chip of Array.from(editor.querySelectorAll<HTMLElement>('.vti-chip'))) {
      const raw = chip.dataset['token'];
      const segment = raw === undefined ? undefined : parseVariableSegments(raw)[0];
      if (raw === undefined || !segment || segment.kind === 'text') continue;
      const unknown = known.size > 0
        && !known.has(variableTokenLabel(segment.kind, segment.name));
      chip.classList.toggle('vti-chip-unknown', unknown);
      chip.title = this.chipTitle(raw, unknown);
    }
    this.renderedNames = known;
  }

  private buildNodes(value: string): Node[] {
    const nodes: Node[] = [];
    const known = this.knownNames();
    const isChip = (node: Node | undefined): boolean =>
      node !== undefined && node.nodeType === Node.ELEMENT_NODE
      && (node as HTMLElement).dataset['token'] !== undefined;

    for (const segment of parseVariableSegments(value)) {
      if (segment.kind === 'text') {
        if (segment.text) nodes.push(document.createTextNode(segment.text));
        continue;
      }
      if (nodes.length === 0 || isChip(nodes[nodes.length - 1])) {
        nodes.push(this.buildCaretAnchor());
      }
      nodes.push(this.buildChip(segment.kind, segment.name, segment.raw, known));
    }

    if (isChip(nodes[nodes.length - 1])) {
      nodes.push(this.buildCaretAnchor());
    }

    if (value.endsWith('\n')) {
      const sentinel = document.createElement('br');
      sentinel.dataset['sentinel'] = '';
      nodes.push(sentinel);
    }
    return nodes;
  }

  private buildCaretAnchor(): Text {
    return document.createTextNode(VariableTextInputComponent.CARET_ANCHOR);
  }

  private buildChip(
    kind: VariableTokenKind,
    name: string,
    raw: string,
    known: ReadonlySet<string>,
  ): HTMLElement {
    const label = variableTokenLabel(kind, name);
    const chip = document.createElement('span');
    chip.className = 'vti-chip';
    chip.contentEditable = 'false';
    chip.dataset['token'] = raw;
    if (kind === 'event') chip.classList.add('vti-chip-event');
    const unknown = known.size > 0 && !known.has(label);
    chip.classList.toggle('vti-chip-unknown', unknown);
    chip.title = this.chipTitle(raw, unknown);

    const text = document.createElement('span');
    text.className = 'vti-chip-label';
    text.textContent = label;
    chip.appendChild(text);

    if (!this.disabled) {
      const remove = document.createElement('span');
      remove.className = 'vti-chip-remove icon icon-x icon-xs';
      remove.setAttribute('aria-hidden', 'true');
      chip.appendChild(remove);
    }
    return chip;
  }

  private serialize(root: Node, chipMask?: string): string {
    let out = '';

    const visit = (node: Node): void => {
      for (const child of Array.from(node.childNodes)) {
        if (child.nodeType === Node.TEXT_NODE) {
          out += stripCaretAnchors((child as Text).data);
          continue;
        }
        if (child.nodeType !== Node.ELEMENT_NODE) continue;

        const element = child as HTMLElement;
        if (element.dataset['sentinel'] !== undefined) continue;

        const token = element.dataset['token'];
        if (token !== undefined) {
          out += chipMask ?? token;
          continue;
        }
        if (element.tagName === 'BR') {
          out += '\n';
          continue;
        }
        if (VariableTextInputComponent.BLOCK_TAGS.has(element.tagName)) {
          if (out.length > 0 && !out.endsWith('\n')) out += '\n';
          if (element.childNodes.length === 1 && element.firstChild?.nodeName === 'BR') continue;
        }
        visit(element);
      }
    };

    visit(root);
    return out;
  }

  private hasLooseToken(editor: HTMLElement): boolean {
    return hasVariableToken(this.serialize(editor, VariableTextInputComponent.CHIP_MASK));
  }

  private offsetOf(container: Node, offset: number): number | null {
    const editor = this.editorRef?.nativeElement;
    if (!editor || !editor.contains(container)) return null;
    const range = document.createRange();
    range.selectNodeContents(editor);
    try {
      range.setEnd(container, offset);
    } catch {
      return null;
    }
    return this.serialize(range.cloneContents()).length;
  }

  private offsetBefore(node: Node): number | null {
    const editor = this.editorRef?.nativeElement;
    if (!editor || !editor.contains(node)) return null;
    const range = document.createRange();
    range.selectNodeContents(editor);
    range.setEndBefore(node);
    return this.serialize(range.cloneContents()).length;
  }

  private selectionOffsets(): TextRange | null {
    const selection = window.getSelection();
    if (!selection || selection.rangeCount === 0) return null;
    const range = selection.getRangeAt(0);
    const start = this.offsetOf(range.startContainer, range.startOffset);
    const end = this.offsetOf(range.endContainer, range.endOffset);
    if (start === null || end === null) return null;
    return { start: Math.min(start, end), end: Math.max(start, end) };
  }

  private currentSelection(): TextRange {
    const length = this.valueState().length;
    const live = this.editorRef?.nativeElement === document.activeElement
      ? this.selectionOffsets()
      : null;
    const selection = live ?? this.lastSelection ?? { start: length, end: length };
    const start = Math.min(selection.start, length);
    return { start, end: Math.min(Math.max(selection.end, start), length) };
  }

  private rememberSelection(): void {
    const selection = this.selectionOffsets();
    if (selection) this.lastSelection = selection;
  }

  private setCaret(offset: number): void {
    const editor = this.editorRef?.nativeElement;
    const selection = window.getSelection();
    if (!editor || !selection) return;

    const range = document.createRange();
    let remaining = offset;
    let placed = false;

    const visit = (node: Node): void => {
      for (const child of Array.from(node.childNodes)) {
        if (placed) return;
        if (child.nodeType === Node.TEXT_NODE) {
          const data = (child as Text).data;
          const length = stripCaretAnchors(data).length;
          if (remaining <= length) {
            range.setStart(child, caretAnchorAwareOffset(data, remaining));
            placed = true;
            return;
          }
          remaining -= length;
          continue;
        }
        if (child.nodeType !== Node.ELEMENT_NODE) continue;

        const element = child as HTMLElement;
        if (element.dataset['sentinel'] !== undefined) continue;

        const token = element.dataset['token'];
        if (token !== undefined) {
          if (remaining <= 0) {
            range.setStartBefore(element);
            placed = true;
            return;
          }
          if (remaining < token.length) {
            range.setStartAfter(element);
            placed = true;
            return;
          }
          remaining -= token.length;
          continue;
        }
        if (element.tagName === 'BR') {
          if (remaining <= 0) {
            range.setStartBefore(element);
            placed = true;
            return;
          }
          remaining -= 1;
          continue;
        }
        visit(element);
      }
    };

    visit(editor);
    if (placed) {
      range.collapse(true);
    } else {
      range.selectNodeContents(editor);
      range.collapse(false);
    }
    selection.removeAllRanges();
    selection.addRange(range);
  }

  private syncSuggestions(): void {
    if (this.disabled) {
      this.closeSuggestions();
      return;
    }
    const selection = this.selectionOffsets();
    if (!selection || selection.start !== selection.end) {
      this.closeSuggestions();
      return;
    }

    const trigger = findVariableTokenTrigger(this.valueState().slice(0, selection.start));
    if (!trigger) {
      this.dismissed = null;
      this.closeSuggestions();
      return;
    }
    if (this.dismissed
      && this.dismissed.start === trigger.start
      && this.dismissed.query === trigger.query) {
      return;
    }
    this.dismissed = null;

    const matches = this.suggestionsFor(trigger);
    if (matches.length === 0) {
      this.closeSuggestions();
      return;
    }

    const unchanged = this.isOpen()
      && this.trigger?.start === trigger.start
      && this.sameSuggestions(matches);
    this.trigger = trigger;
    if (unchanged) return;

    this.suggestions.set(matches);
    this.activeIndex.set(0);
    this.caretPoint.set(this.caretViewportPoint());
    this.isOpen.set(true);
  }

  private sameSuggestions(next: VariableSuggestion[]): boolean {
    const current = this.suggestions();
    return current.length === next.length
      && current.every((suggestion, index) => suggestion.token === next[index]?.token);
  }

  private suggestionsFor(trigger: VariableTokenTrigger): VariableSuggestion[] {
    const accepted = this.acceptedTypesState();
    const suggestions: VariableSuggestion[] = [];

    for (const variable of this.variablesState()) {
      if (suggestions.length >= MAX_SUGGESTIONS) break;
      const kind = variableTokenKind(variable);
      if (trigger.kind && trigger.kind !== kind) continue;
      if (accepted && !accepted.includes(variable.type)) continue;
      if (!matchesVariableTokenQuery(kind, variable.name, trigger.query)) continue;
      suggestions.push({
        kind,
        name: variable.name,
        label: variableTokenLabel(kind, variable.name),
        token: variableTokenText(kind, variable.name),
        meta: kind === 'event' ? variable.type : variable.value,
      });
    }
    return suggestions;
  }

  private caretViewportPoint(): { x: number; y: number } {
    const selection = window.getSelection();
    const rect = selection && selection.rangeCount > 0
      ? selection.getRangeAt(0).getClientRects()[0]
      : undefined;
    if (rect) return { x: rect.left, y: rect.bottom + 2 };

    const fallback = this.editorRef?.nativeElement.getBoundingClientRect();
    return fallback ? { x: fallback.left, y: fallback.bottom + 2 } : { x: 0, y: 0 };
  }

  private chipTitle(raw: string, unknown: boolean): string {
    return unknown
      ? this.localization.translateKey(AppStrings.Forms.VariableTextInput.UnknownVariable, { raw })
      : raw;
  }
}

function sameNames(a: ReadonlySet<string>, b: ReadonlySet<string>): boolean {
  if (a === b) return true;
  if (a.size !== b.size) return false;
  for (const name of a) {
    if (!b.has(name)) return false;
  }
  return true;
}

function codePointWidth(value: string, offset: number, delta: 1 | -1): number {
  if (delta > 0) {
    const code = value.charCodeAt(offset);
    return code >= 0xD800 && code <= 0xDBFF ? 2 : 1;
  }
  const previous = value.charCodeAt(offset - 1);
  const beforeThat = offset >= 2 ? value.charCodeAt(offset - 2) : 0;
  const isTrailing = previous >= 0xDC00 && previous <= 0xDFFF;
  const isLeading = beforeThat >= 0xD800 && beforeThat <= 0xDBFF;
  return isTrailing && isLeading ? 2 : 1;
}

const CARET_ANCHOR_PATTERN = /\u200B/g;

function stripCaretAnchors(data: string): string {
  return data.replace(CARET_ANCHOR_PATTERN, '');
}

function caretAnchorAwareOffset(data: string, modelOffset: number): number {
  let offset = 0;
  let counted = 0;
  while (offset < data.length) {
    if (data[offset] !== '\u200B') {
      if (counted === modelOffset) break;
      counted++;
    }
    offset++;
  }
  return offset;
}
