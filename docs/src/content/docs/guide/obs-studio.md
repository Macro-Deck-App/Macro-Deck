---
title: OBS Studio
description: How Macro Deck follows changes in OBS Studio and reacts to events your OBS scripts send.
---

Macro Deck connects to OBS Studio through its built-in WebSocket server (obs-websocket 5.x). Add the
connection under **Integrations**, then use OBS actions, variables and triggers in your deck.

## Changes show up quickly

Macro Deck listens to what OBS reports instead of only asking again every second. Variables that follow
an input's settings, whether an input is active or showing, and whether a filter is enabled refresh
within a fraction of a second after you change them in OBS.

Buttons that follow OBS state (for example a mute or filter toggle) read the current value the next time
the button refreshes, so they can lag by up to a couple of seconds. Variables are the fastest way to see
a change.

Macro Deck never listens to the per-frame OBS events (audio level meters and scene item transforms),
so a busy OBS does not slow Macro Deck down.

## Triggers

Add one of these as an event trigger on a button or flow. Leave **Input** empty to react to any input.

| Trigger | Fires when |
| --- | --- |
| Input Became Active | An input starts rendering in the program output, for example a webcam going live |
| Input Became Inactive | An input stops rendering in the program output |
| Input Started Showing | An input starts rendering in the program or the preview output |
| Input Stopped Showing | An input is no longer rendered in the program or the preview output |
| Custom Event | An OBS script or plugin sends a custom event, see below |

## Custom events

Any program that is connected to OBS can broadcast a custom event with the obs-websocket request
`BroadcastCustomEvent`. Macro Deck turns each one into a **Custom Event** trigger.

Send a JSON object. Put a name in `eventName` to filter on it. Leave **Event Name** on the trigger empty
to react to every custom event.

This Node.js script (version 22 or newer) sends one event:

```js
import { createHash } from 'node:crypto';

const sha = (text) => createHash('sha256').update(text).digest('base64');
const ws = new WebSocket('ws://127.0.0.1:4455');

ws.onmessage = ({ data }) => {
  const { op, d } = JSON.parse(data);
  if (op === 0) {
    const identify = { rpcVersion: 1, eventSubscriptions: 0 };
    if (d.authentication) {
      const secret = sha(process.env.OBS_PASSWORD + d.authentication.salt);
      identify.authentication = sha(secret + d.authentication.challenge);
    }
    ws.send(JSON.stringify({ op: 1, d: identify }));
  } else if (op === 2) {
    ws.send(JSON.stringify({
      op: 6,
      d: {
        requestType: 'BroadcastCustomEvent',
        requestId: '1',
        requestData: { eventData: { eventName: 'goal', team: 'red', score: 3 } },
      },
    }));
  } else if (op === 7) {
    ws.close();
  }
};
```

### What the flow receives

| Value | Contains |
| --- | --- |
| `event.eventName` | The `eventName` text, or empty when there is none |
| `event.data` | The whole object as compact JSON text |
| `event.field_<name>` | A top level text, number or true/false property, for example `event.field_team` |

Use the values in templates such as `{{ event.field_score }}`. To react only when a field has a certain
value, add a condition on that value to the trigger. The `field_` values do not appear in the editor's
value picker, so type them in.

### Limits

Custom events come from outside Macro Deck, so they are checked:

- An event larger than 16 KiB, nested deeper than 32 levels or not valid JSON is ignored.
- Names are cut at 256 characters and text values at 1024 characters.
- At most 16 fields are exposed, and only properties named with letters, digits and underscores.
  Nested objects and arrays are only available inside `event.data`.
- More than about 20 events per second are dropped. A short burst is allowed.
- Values are only ever used as plain text. They are never run as code or opened as a path.
