import { HttpClient, HttpParams } from '@angular/common/http';
import { computed, effect, inject, Injectable, InjectionToken, OnDestroy, signal, untracked } from '@angular/core';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { firstValueFrom, forkJoin, Subscription } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { API_BASE_URL } from '../../core/config/api.config';
import { Notification, NotificationPage } from './notification.models';

export interface NotificationConnection {
  start(): Promise<void>;
  stop(): Promise<void>;
  on(name: string, callback: () => void): void;
  onreconnecting(callback: () => void): void;
  onreconnected(callback: () => void): void;
  onclose(callback: () => void): void;
}

export const NOTIFICATION_CONNECTION_FACTORY = new InjectionToken<
  (token: () => Promise<string>) => NotificationConnection
>('Notification connection factory', {
  providedIn: 'root',
  factory: () => token => new HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/notifications`, { accessTokenFactory: token })
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .configureLogging(LogLevel.None).build(),
});

@Injectable({ providedIn: 'root' })
export class NotificationService implements OnDestroy {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly createConnection = inject(NOTIFICATION_CONNECTION_FACTORY);
  readonly items = signal<Notification[]>([]);
  readonly unreadCount = signal(0);
  readonly totalCount = signal(0);
  readonly page = signal(1);
  readonly unreadOnly = signal(false);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly status = signal<'offline' | 'connecting' | 'live' | 'reconnecting'>('offline');
  readonly pages = computed(() => Math.max(1, Math.ceil(this.totalCount() / 20)));
  private userId: string | undefined;
  private connection: NotificationConnection | undefined;
  private generation = 0;
  private polling: ReturnType<typeof setInterval> | undefined;
  private retry: ReturnType<typeof setTimeout> | undefined;
  private request: Subscription | undefined;

  constructor() {
    effect(() => {
      const userId = this.auth.currentUser()?.userId;
      untracked(() => this.setUser(userId));
    });
  }

  reload(): void {
    if (!this.userId) return;
    this.request?.unsubscribe();
    this.loading.set(true);
    const params = new HttpParams().set('page', this.page()).set('pageSize', 20).set('unreadOnly', this.unreadOnly());
    this.request = forkJoin({
      page: this.http.get<NotificationPage>(`${API_BASE_URL}/notifications`, { params }),
      summary: this.http.get<{ unreadCount: number }>(`${API_BASE_URL}/notifications/summary`),
    }).subscribe({
      next: result => {
        this.items.set(result.page.items); this.totalCount.set(result.page.totalCount);
        this.unreadCount.set(result.summary.unreadCount); this.loading.set(false); this.error.set('');
        if (this.page() > this.pages()) { this.page.set(this.pages()); this.reload(); }
      },
      error: () => {
        // Do not keep showing tenant data after a failed authorization/access refresh.
        this.items.set([]); this.unreadCount.set(0); this.totalCount.set(0);
        this.loading.set(false); this.error.set('Notifications could not be loaded. Try again.');
      },
    });
  }

  filterUnread(value: boolean): void { this.unreadOnly.set(value); this.page.set(1); this.reload(); }
  movePage(delta: number): void { this.page.set(Math.max(1, Math.min(this.pages(), this.page() + delta))); this.reload(); }

  markRead(id: string): void {
    const userId = this.userId;
    this.http.put<void>(`${API_BASE_URL}/notifications/${id}/read`, {}).subscribe({
      next: () => { if (userId === this.userId) this.reload(); },
      error: () => { if (userId === this.userId) this.error.set('This notification could not be marked as read.'); },
    });
  }

  markAllRead(): void {
    const userId = this.userId;
    this.http.put<void>(`${API_BASE_URL}/notifications/read-all`, {}).subscribe({
      next: () => { if (userId === this.userId) this.reload(); },
      error: () => { if (userId === this.userId) this.error.set('Notifications could not be marked as read.'); },
    });
  }

  private setUser(userId: string | undefined): void {
    if (this.userId === userId) return;
    this.generation++;
    this.userId = userId;
    this.request?.unsubscribe();
    clearInterval(this.polling); clearTimeout(this.retry);
    const old = this.connection; this.connection = undefined;
    void old?.stop().catch(() => {});
    this.items.set([]); this.unreadCount.set(0); this.totalCount.set(0); this.page.set(1);
    this.unreadOnly.set(false); this.error.set(''); this.loading.set(false); this.status.set('offline');
    if (!userId) return;
    this.reload();
    this.polling = setInterval(() => this.reload(), 30000);
    void this.connect(this.generation);
  }

  private async connect(generation: number): Promise<void> {
    if (!this.userId || generation !== this.generation) return;
    this.status.set('connecting');
    const connection = this.createConnection(async () => {
      if (generation !== this.generation || !this.auth.currentUser()) throw new Error('Session ended.');
      const session = this.auth.currentUser()!;
      if (Date.parse(session.accessTokenExpiresAtUtc) <= Date.now() + 30000)
        await firstValueFrom(this.auth.refresh());
      if (generation !== this.generation) throw new Error('Session changed.');
      return this.auth.accessToken ?? '';
    });
    this.connection = connection;
    const current = () => generation === this.generation && this.connection === connection;
    connection.on('NotificationsChanged', () => { if (current()) this.reload(); });
    connection.onreconnecting(() => { if (current()) this.status.set('reconnecting'); });
    connection.onreconnected(() => { if (current()) { this.status.set('live'); this.reload(); } });
    connection.onclose(() => {
      if (current()) { this.status.set('offline'); this.scheduleRetry(generation); }
    });
    try {
      await connection.start();
      if (!current()) { await connection.stop(); return; }
      this.status.set('live'); this.reload();
    } catch {
      if (current()) { this.status.set('offline'); this.scheduleRetry(generation); }
    }
  }

  private scheduleRetry(generation: number): void {
    clearTimeout(this.retry);
    this.retry = setTimeout(() => { void this.connect(generation); }, 15000);
  }

  ngOnDestroy(): void { this.setUser(undefined); }
}
