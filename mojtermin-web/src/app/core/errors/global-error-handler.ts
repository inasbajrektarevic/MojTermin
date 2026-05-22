import { ErrorHandler, Injectable, NgZone, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import * as Sentry from '@sentry/angular';
import { ToastService } from '../services/toast.service';
import { environment } from '../../../environments/environment';

/**
 * Catches errors that escape the Angular zone (template runtime errors,
 * uncaught promise rejections from non-HTTP sources, programming bugs).
 *
 * HTTP errors are already surfaced by errorInterceptor with structured
 * server messages, so we explicitly skip HttpErrorResponse here to avoid
 * showing two toasts for the same problem.
 */
@Injectable()
export class GlobalErrorHandler implements ErrorHandler {
  private readonly toastService = inject(ToastService);
  private readonly zone = inject(NgZone);

  handleError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (!environment.production) {
        console.error('[HTTP error already handled by interceptor]', error);
      }
      return;
    }

    if (!environment.production) {
      console.error('[Uncaught application error]', error);
    }

    // Report to Sentry first so we still get the diagnostic even if toast
    // logic later throws. captureException is a no-op when Sentry was not
    // initialised (empty DSN in main.ts).
    if (environment.sentryDsn) {
      Sentry.captureException(error);
    }

    const message = this.buildMessage(error);

    // Errors thrown outside the Angular zone (e.g. inside microtasks) need to
    // be re-entered so the toast signal triggers change detection.
    this.zone.run(() => this.toastService.error(message));
  }

  private buildMessage(error: unknown): string {
    if (environment.production) {
      return 'Došlo je do neočekivane greške. Pokušajte ponovo ili osvježite stranicu.';
    }

    if (error instanceof Error) {
      return `Greška: ${error.message}`;
    }

    if (typeof error === 'string' && error.trim()) {
      return `Greška: ${error}`;
    }

    return 'Došlo je do neočekivane greške.';
  }
}
