import { provideZonelessChangeDetection } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { BehaviorSubject } from 'rxjs';

import { ApiService, ConnectionState } from '../transport';
import { VideoStreamService } from './video-stream.service';

describe('VideoStreamService', () => {
  let state: BehaviorSubject<ConnectionState>;
  let request: jasmine.Spy<(type: string, payload: unknown) => Promise<unknown>>;

  beforeEach(() => {
    state = new BehaviorSubject<ConnectionState>('connected');
    request = jasmine.createSpy('request');
    TestBed.configureTestingModule({
      providers: [
        provideZonelessChangeDetection(),
        {
          provide: ApiService,
          useValue: {
            request,
            onAnyNotification: () => () => undefined,
            connectionState$: state,
            get isConnected() { return state.value === 'connected'; },
          },
        },
      ],
    });
  });

  function listener() {
    return jasmine.createSpyObj('listener', ['changed', 'closed']);
  }

  it('hands the host refusal code to the view', async () => {
    request.and.returnValue(Promise.reject({ code: 'unknown_stream' }));
    const closed = listener();

    TestBed.inject(VideoStreamService).surface().client.open('p::a', 'front', ['mjpeg'], closed);
    await new Promise(resolve => setTimeout(resolve));

    expect(request).toHaveBeenCalledWith('OpenVideoStream',
      { providerId: 'p::a', streamId: 'front', acceptedTransports: ['mjpeg'] });
    expect(closed.closed).toHaveBeenCalledWith('failed', 'unknown_stream', null);
  });

  it('ends open sessions when the connection to the host drops', async () => {
    request.and.returnValue(Promise.resolve({ sessionId: 's1', revision: 0, state: 'opening' }));
    const session = listener();
    TestBed.inject(VideoStreamService).surface().client.open('p::a', 'front', ['mjpeg'], session);
    await new Promise(resolve => setTimeout(resolve));

    state.next('reconnecting');

    expect(session.closed).toHaveBeenCalledWith('consumer_disconnected', null, null);
  });
});
