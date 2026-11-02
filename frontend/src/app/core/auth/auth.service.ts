import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { EMPTY, Observable, catchError, defer, endWith, finalize, ignoreElements, map, of, shareReplay, switchMap } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { AuthResponse, LoginRequest, RegisterRequest } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly session = signal<AuthResponse | null>(null);
  private refreshInFlight$: Observable<AuthResponse> | undefined;
  private pendingSessionRequest$: Observable<unknown> | undefined;
  private identityRevision = 0;

  get sessionRevision(): number { return this.identityRevision; }

  readonly currentUser = this.session.asReadonly();

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  get isAuthenticated(): boolean {
    return this.session() !== null;
  }

  login(credentials: LoginRequest): Observable<AuthResponse> {
    return this.startSession('login', credentials);
  }

  register(input: RegisterRequest): Observable<AuthResponse> {
    return this.startSession('register', input);
  }

  private startSession(endpoint: string, input: LoginRequest | RegisterRequest): Observable<AuthResponse> {
    return defer(() => {
      this.clearSession();
      const revision = this.identityRevision;
      return this.queueSessionRequest(() => this.http.post<AuthResponse>(`${API_BASE_URL}/auth/${endpoint}`, input,
        { withCredentials: true })).pipe(map(response => this.acceptSession(response, revision)));
    });
  }

  refresh(): Observable<AuthResponse> {
    if (!this.refreshInFlight$) {
      const request$ = defer(() => {
        const revision = this.identityRevision;
        return this.queueSessionRequest(() => this.http.post<AuthResponse>(`${API_BASE_URL}/auth/refresh`, {},
          { withCredentials: true })).pipe(map(response => this.acceptSession(response, revision)));
      }).pipe(
        finalize(() => { if (this.refreshInFlight$ === request$) this.refreshInFlight$ = undefined; }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
      this.refreshInFlight$ = request$;
    }
    return this.refreshInFlight$;
  }

  logout(): Observable<void> {
    return defer(() => {
      this.clearSession();
      return this.queueSessionRequest(() => this.http.post<void>(`${API_BASE_URL}/auth/logout`, {},
        { withCredentials: true }));
    });
  }

  clearSession(): void {
    this.identityRevision++;
    this.session.set(null);
    this.refreshInFlight$ = undefined;
  }

  private acceptSession(response: AuthResponse, revision: number): AuthResponse {
    if (revision !== this.identityRevision) throw new Error('Session changed while authentication was in flight.');
    this.session.set(response);
    return response;
  }

  private queueSessionRequest<T>(send: () => Observable<T>): Observable<T> {
    // Cookie responses must arrive in request order, including when a caller
    // unsubscribes. Ignoring a stale response body alone cannot undo Set-Cookie.
    const previous = this.pendingSessionRequest$;
    const ready$ = previous
      ? previous.pipe(ignoreElements(), catchError(() => EMPTY), endWith(null))
      : of(null);
    const request$ = ready$.pipe(
      switchMap(() => send()),
      finalize(() => { if (this.pendingSessionRequest$ === request$) this.pendingSessionRequest$ = undefined; }),
      shareReplay({ bufferSize: 1, refCount: false }),
    );
    this.pendingSessionRequest$ = request$;
    return request$;
  }
}
