import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';

import { ActionBlock } from '@macro-deck/runtime';
import { ActionClipboardService, sameActionFlowOwner } from './action-clipboard.service';

function block(overrides: Partial<ActionBlock> = {}): ActionBlock {
  return {
    id: 'block-1',
    type: 'action',
    blockType: 'system.kill-process',
    label: 'Kill Application',
    color: '#000',
    parameters: [{ name: 'processName', type: 'string', label: 'Process', value: 'Steam' }],
    ...overrides,
  };
}

describe('ActionClipboardService', () => {
  let service: ActionClipboardService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideZonelessChangeDetection()] });
    service = TestBed.inject(ActionClipboardService);
  });

  it('starts empty', () => {
    expect(service.entry()).toBeNull();
    expect(service.hasContent()).toBeFalse();
    expect(service.cutBlockId()).toBeNull();
  });

  it('holds a snapshot on copy, without an origin to settle', () => {
    service.copy(block());

    const entry = service.entry()!;
    expect(entry.block.label).toBe('Kill Application');
    expect(entry.isCut).toBeFalse();
    expect(entry.origin).toBeNull();
    expect(service.hasContent()).toBeTrue();
    expect(service.cutBlockId()).toBeNull();
  });

  it('deep-copies the block so later edits to the original do not leak in', () => {
    const source = block();
    service.copy(source);

    source.parameters![0].value = 'changed';

    expect(service.entry()!.block.parameters![0].value as string).toBe('Steam');
  });

  it('records the origin on cut and names the source block', () => {
    service.cut(block(), { kind: 'widget', widgetId: 'w1' });

    const entry = service.entry()!;
    expect(entry.isCut).toBeTrue();
    expect(entry.origin).toEqual({ owner: { kind: 'widget', widgetId: 'w1' }, blockId: 'block-1' });
    expect(entry.pendingPasteOwner).toBeNull();
    expect(service.cutBlockId()).toBe('block-1');
  });

  it('remembers where a cut was pasted, so the move can be settled when that record saves', () => {
    service.cut(block(), { kind: 'widget', widgetId: 'w1' });

    service.markPasted({ kind: 'script', scriptId: 's1' }, 'pasted-1');

    expect(service.entry()!.pendingPasteOwner).toEqual({ kind: 'script', scriptId: 's1' });
    expect(service.entry()!.pendingPasteBlockId).toBe('pasted-1');
  });

  it('ignores a paste marker for a copy - there is no source to remove', () => {
    service.copy(block());

    service.markPasted({ kind: 'widget', widgetId: 'w2' }, 'pasted-1');

    expect(service.entry()!.pendingPasteOwner).toBeNull();
  });

  it('clears', () => {
    service.cut(block(), { kind: 'widget', widgetId: 'w1' });

    service.clear();

    expect(service.entry()).toBeNull();
    expect(service.cutBlockId()).toBeNull();
  });
});

describe('sameActionFlowOwner', () => {
  it('matches identical owners of the same kind', () => {
    expect(sameActionFlowOwner({ kind: 'widget', widgetId: 'w1' }, { kind: 'widget', widgetId: 'w1' }))
      .toBeTrue();
  });

  it('separates different records and different kinds', () => {
    expect(sameActionFlowOwner({ kind: 'widget', widgetId: 'w1' }, { kind: 'widget', widgetId: 'w2' }))
      .toBeFalse();
    expect(sameActionFlowOwner({ kind: 'script', scriptId: 'x' }, { kind: 'automation', automationId: 'x' }))
      .toBeFalse();
  });

  it('never matches a missing owner', () => {
    expect(sameActionFlowOwner(null, null)).toBeFalse();
    expect(sameActionFlowOwner(null, { kind: 'widget', widgetId: 'w1' })).toBeFalse();
  });
});
