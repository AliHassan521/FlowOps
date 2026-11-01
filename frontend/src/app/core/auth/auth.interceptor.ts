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

  return next(addAccessToken(request, authService.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error) || isAuthEndpoint) {
        return throwError(() => error);
      }

      return authService.refresh().pipe(
        catchError((refreshError) => {
          authService.clearSession();
          return throwError(() => refreshError);
        }),
        switchMap(() => next(addAccessToken(request, authService.accessToken))),
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
