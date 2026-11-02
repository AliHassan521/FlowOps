import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { API_BASE_URL } from '../config/api.config';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const base = new URL(API_BASE_URL, window.location.origin);
  const target = new URL(request.url, window.location.origin);
  if (target.origin !== base.origin || !target.pathname.startsWith(`${base.pathname}/`)) return next(request);
  const isAuthEndpoint = target.pathname.startsWith(`${base.pathname}/auth/`);

  const revision = authService.sessionRevision;
  const token = authService.accessToken;
  return next(addAccessToken(request, token)).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error) || isAuthEndpoint || !token || revision !== authService.sessionRevision) {
        return throwError(() => error);
      }

      return authService.refresh().pipe(
        catchError((refreshError) => {
          if (revision === authService.sessionRevision && isAuthenticationFailure(refreshError)) authService.clearSession();
          return throwError(() => refreshError);
        }),
        switchMap(() => revision === authService.sessionRevision
          ? next(addAccessToken(request, authService.accessToken))
          : throwError(() => error)),
      );
    }),
  );
};

function addAccessToken(request: HttpRequest<unknown>, token: string | null): HttpRequest<unknown> {
  return token
    ? request.clone({ setHeaders: { Authorization: `Bearer ${token}` }, withCredentials: true })
    : request.clone({ withCredentials: true });
}

function isUnauthorized(error: unknown): error is { status: number } {
  return typeof error === 'object' && error !== null && 'status' in error && error.status === 401;
}

function isAuthenticationFailure(error: unknown): boolean {
  return typeof error === 'object' && error !== null && 'status' in error
    && (error.status === 401 || error.status === 403);
}
