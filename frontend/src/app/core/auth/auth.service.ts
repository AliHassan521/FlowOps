import { HttpClient } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { Observable, finalize, shareReplay, tap } from 'rxjs';
import { API_BASE_URL } from '../config/api.config';
import { AuthResponse, LoginRequest } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly session = signal<AuthResponse | null>(null);
  private refreshInFlight$: Observable<AuthResponse> | undefined;

  readonly currentUser = this.session.asReadonly();

  get accessToken(): string | null {
    return this.session()?.accessToken ?? null;
  }

  get isAuthenticated(): boolean {
    return this.session() !== null;
  }

  login(credentials: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_BASE_URL}/auth/login`, credentials, { withCredentials: true })
      .pipe(tap((response) => this.session.set(response)));
  }

  refresh(): Observable<AuthResponse> {
    if (!this.refreshInFlight$) {
      this.refreshInFlight$ = this.http
        .post<AuthResponse>(`${API_BASE_URL}/auth/refresh`, {}, { withCredentials: true })
        .pipe(
          tap((response) => this.session.set(response)),
          finalize(() => (this.refreshInFlight$ = undefined)),
          shareReplay({ bufferSize: 1, refCount: false }),
        );
    }

    return this.refreshInFlight$;
  }

  logout(): Observable<void> {
    return this.http
      .post<void>(`${API_BASE_URL}/auth/logout`, {}, { withCredentials: true })
      .pipe(finalize(() => this.clearSession()));
  }

  clearSession(): void {
    this.session.set(null);
  }
}
