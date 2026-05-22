import { bootstrapApplication } from '@angular/platform-browser';
import * as Sentry from '@sentry/angular';
import { appConfig } from './app/app.config';
import { AppComponent } from './app/app.component';
import { environment } from './environments/environment';

// Sentry is fully opt-in: an empty DSN keeps the SDK silent so local
// development has no outbound telemetry. Production builds inject a DSN
// via environment.prod.ts at deploy time.
if (environment.sentryDsn) {
  Sentry.init({
    dsn: environment.sentryDsn,
    environment: environment.sentryEnvironment,
    tracesSampleRate: environment.sentryTracesSampleRate,
    // No replay/session capture by default — we'd rather opt-in explicitly
    // because clients are entering booking PII and we don't want to ship
    // that to Sentry without a privacy review.
  });
}

bootstrapApplication(AppComponent, appConfig)
  .catch((err) => console.error(err));
