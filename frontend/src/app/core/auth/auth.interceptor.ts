import { HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);
  const isAuthEndpoint = request.url.includes('/auth/');

  return next(addAccessToken(request, authService.accessToken)).pipe(
    catchError((error: unknown) => {
      if (!isUnauthorized(error) || isAuthEndpoint) {
        return throwError(() => error);
      }

      return authService.refresh().pipe(
        switchMap(() => next(addAccessToken(request, authService.accessToken))),
        catchError((refreshError) => {
          authService.clearSession();
          return throwError(() => refreshError);
        }),
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
