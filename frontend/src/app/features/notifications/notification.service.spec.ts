import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api.config';
import { NOTIFICATION_CONNECTION_FACTORY, NotificationConnection, NotificationService } from './notification.service';

describe('NotificationService', () => {
  const user = { userId: 'recipient', accessToken: 'token', accessTokenExpiresAtUtc: '2099-01-01T00:00:00Z' };
  const currentUser = signal<typeof user | null>(user);
  let service: NotificationService;
  let http: HttpTestingController;
  let callbacks: Record<string, () => void>;
  let connection: NotificationConnection;
  let factory: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers(); currentUser.set(user); callbacks = {};
    connection = {
      start: vi.fn().mockReturnValue(new Promise<void>(() => {})), stop: vi.fn().mockResolvedValue(undefined),
      on: (name, callback) => callbacks[name] = callback,
      onreconnecting: callback => callbacks['reconnecting'] = callback,
      onreconnected: callback => callbacks['reconnected'] = callback,
      onclose: callback => callbacks['closed'] = callback,
    };
    factory = vi.fn().mockReturnValue(connection);
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting(),
      { provide: AuthService, useValue: { currentUser, get accessToken() { return currentUser()?.accessToken; } } },
      { provide: NOTIFICATION_CONNECTION_FACTORY, useValue: factory },
    ] });
    service = TestBed.inject(NotificationService); http = TestBed.inject(HttpTestingController);
    TestBed.tick();
  });
  afterEach(() => { service.ngOnDestroy(); http.verify(); vi.useRealTimers(); });

  function flushReload(count = 1): void {
    http.expectOne(request => request.url === `${API_BASE_URL}/notifications`).flush({
      items: count ? [{ id: 'notification-1', message: 'You were assigned: Ship safely', readAtUtc: null }] : [],
      totalCount: count, page: 1, pageSize: 20,
    });
    http.expectOne(`${API_BASE_URL}/notifications/summary`).flush({ unreadCount: count });
  }

  it('loads persisted notifications and recovers state on a hint or reconnect', () => {
    flushReload(); expect(service.unreadCount()).toBe(1);
    callbacks['NotificationsChanged'](); flushReload(2);
    expect(service.totalCount()).toBe(2);
    callbacks['reconnecting'](); expect(service.status()).toBe('reconnecting');
    callbacks['reconnected'](); flushReload(3);
    expect(service.status()).toBe('live'); expect(service.unreadCount()).toBe(3);
  });

  it('marks read through the API and reloads authoritative state', () => {
    flushReload(); service.markRead('notification-1');
    const mutation = http.expectOne(`${API_BASE_URL}/notifications/notification-1/read`);
    expect(mutation.request.method).toBe('PUT'); mutation.flush(null);
    flushReload(0); expect(service.unreadCount()).toBe(0);
  });

  it('uses explicit pagination and unread filtering', () => {
    flushReload(42); service.movePage(1);
    const request = http.expectOne(request => request.url === `${API_BASE_URL}/notifications`);
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], totalCount: 42, page: 2, pageSize: 20 });
    http.expectOne(`${API_BASE_URL}/notifications/summary`).flush({ unreadCount: 42 });
    service.filterUnread(true);
    const filtered = http.expectOne(request => request.url === `${API_BASE_URL}/notifications`);
    expect(filtered.request.params.get('page')).toBe('1');
    expect(filtered.request.params.get('unreadOnly')).toBe('true');
    filtered.flush({ items: [], totalCount: 0, page: 1, pageSize: 20 });
    http.expectOne(`${API_BASE_URL}/notifications/summary`).flush({ unreadCount: 0 });
  });

  it('clears private state and stops the connection on logout', () => {
    flushReload(); currentUser.set(null); TestBed.tick();
    expect(service.items()).toEqual([]); expect(service.unreadCount()).toBe(0);
    expect(connection.stop).toHaveBeenCalled();
    callbacks['NotificationsChanged']();
    http.expectNone(request => request.url.includes('/notifications'));
  });

  it('falls back to polling and retries after an exhausted connection', async () => {
    flushReload(); callbacks['closed']();
    expect(service.status()).toBe('offline');
    await vi.advanceTimersByTimeAsync(15000);
    expect(factory).toHaveBeenCalledTimes(2);
    await vi.advanceTimersByTimeAsync(15000); flushReload();
  });
});
