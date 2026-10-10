import { UiNode } from '../ui-framework/ui-node.interface';
import {
  applyNotificationDraftEvent,
  createNotificationDraft,
  overlayNotificationDraft,
} from './notification-form';

function input(id: string, type: string, properties: Record<string, unknown> = {}, children?: UiNode[]): UiNode {
  return { id, type, properties: { events: ['change'], ...properties }, children };
}

function form(): UiNode {
  return {
    id: 'form',
    type: 'ui.stack',
    children: [
      { id: 'label', type: 'ui.text', properties: { text: 'Name' } },
      input('name', 'ui.text-field', { text: 'Ada' }),
      input('subscribed', 'ui.toggle'),
      input('level', 'ui.slider', { level: 0.25 }),
      input('choice', 'ui.segmented', {}, [
        { id: 'a', type: 'ui.text' },
        { id: 'b', type: 'ui.text' },
      ]),
      { id: 'readonly', type: 'ui.text-field', properties: { text: 'fixed' } },
    ],
  };
}

describe('notification form draft', () => {
  it('seeds every input from the tree and nothing else', () => {
    expect(createNotificationDraft(form())).toEqual({
      name: 'Ada',
      subscribed: false,
      level: 0.25,
      choice: null,
    });
  });

  it('records change and adjust events of an input and rejects values of the wrong type', () => {
    const tree = form();
    let draft = createNotificationDraft(tree);

    draft = applyNotificationDraftEvent(tree, draft, { nodeId: 'name', name: 'adjust', data: 'Grace' });
    draft = applyNotificationDraftEvent(tree, draft, { nodeId: 'subscribed', name: 'change', data: true });
    draft = applyNotificationDraftEvent(tree, draft, { nodeId: 'choice', name: 'change', data: 1 });
    const rejected = applyNotificationDraftEvent(tree, draft, { nodeId: 'level', name: 'change', data: 'high' });
    const unknown = applyNotificationDraftEvent(tree, draft, { nodeId: 'label', name: 'change', data: 'x' });
    const readonly = applyNotificationDraftEvent(tree, draft, { nodeId: 'readonly', name: 'change', data: 'x' });

    expect(draft).toEqual({ name: 'Grace', subscribed: true, level: 0.25, choice: 1 });
    expect(rejected).toBe(draft);
    expect(unknown).toBe(draft);
    expect(readonly).toBe(draft);
  });

  it('paints the typed value back into the tree so the control keeps it across a repaint', () => {
    const tree = form();
    const draft = applyNotificationDraftEvent(tree, createNotificationDraft(tree), {
      nodeId: 'name',
      name: 'adjust',
      data: 'Grace',
    });

    const painted = overlayNotificationDraft(tree, { ...draft, subscribed: true, choice: 1 });
    const byId = (id: string) => painted.children!.filter(c => c.id === id)[0];

    expect(byId('name').properties!['text']).toBe('Grace');
    expect(byId('subscribed').properties!['on']).toBe(true);
    expect(byId('choice').properties!['selected']).toBe(1);
    expect(byId('label')).toBe(tree.children![0]);
    expect(tree.children![1].properties!['text']).toBe('Ada');
  });

  it('returns the very same tree when the draft changes nothing, so a repaint is skipped', () => {
    const tree = form();

    expect(overlayNotificationDraft(tree, {})).toBe(tree);
  });
});
