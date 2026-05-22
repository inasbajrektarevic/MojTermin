import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { throwError } from 'rxjs';
import { catchError, switchMap } from 'rxjs/operators';
import { ToastService } from '../services/toast.service';
import { AuthService } from '../auth/auth.service';
import { Router } from '@angular/router';

export const errorInterceptor: HttpInterceptorFn = (req, next) => {
  const toastService = inject(ToastService);
  const authService = inject(AuthService);
  const router = inject(Router);

  return next(req).pipe(
    catchError((error: HttpErrorResponse) => {
      if (shouldTryRefresh(req, error, authService)) {
        return authService.refresh().pipe(
          catchError(() => {
            authService.logout();
            router.navigateByUrl(getTenantAwareLoginUrl(router.url));
            toastService.error('Sesija je istekla. Prijavite se ponovo.');
            return throwError(() => error);
          }),
          switchMap(() => {
            const latestToken = authService.getAccessToken();
            const retriedRequest = req.clone({
              setHeaders: {
                'x-refresh-attempted': 'true',
                ...(latestToken ? { Authorization: `Bearer ${latestToken}` } : {})
              }
            });
            return next(retriedRequest);
          })
        );
      }

      if (error.status === 401 && !req.url.includes('/auth/login')) {
        authService.logout();
        router.navigateByUrl(getTenantAwareLoginUrl(router.url));
      }

      const message = buildErrorMessage(error);
      toastService.error(message);
      return throwError(() => error);
    })
  );
};

function getTenantAwareLoginUrl(currentUrl: string): string {
  const match = currentUrl.match(/\/b\/([^/]+)\/admin(\/|$)/i);
  if (match?.[1]) {
    return `/b/${match[1]}/admin/login`;
  }

  return '/admin/login';
}

function shouldTryRefresh(req: { url: string; headers: { has(name: string): boolean } }, error: HttpErrorResponse, authService: AuthService): boolean {
  if (error.status !== 401) {
    return false;
  }

  if (req.url.includes('/auth/login') || req.url.includes('/auth/refresh')) {
    return false;
  }

  if (req.headers.has('x-refresh-attempted')) {
    return false;
  }

  return authService.hasRefreshToken();
}

function buildErrorMessage(error: HttpErrorResponse): string {
  if (typeof error.error === 'string' && error.error.trim()) {
    return error.error;
  }

  const payload = error.error as
    | { message?: string; title?: string; errors?: Record<string, string[]> }
    | null
    | undefined;

  if (payload?.message) {
    const details = (payload as { details?: string }).details?.trim();
    if (details && details !== payload.message) {
      return `${payload.message} (${details})`;
    }
    return payload.message;
  }

  // ASP.NET Core validation problem details (400) shape.
  if (payload?.errors && typeof payload.errors === 'object') {
    const allMessages = Object.values(payload.errors).flat().filter(Boolean);
    if (allMessages.length) {
      return allMessages.join(' | ');
    }
  }

  if (payload?.title) {
    return payload.title;
  }

  return 'Došlo je do greške pri komunikaciji sa serverom.';
}
