export const environment = {
  production: false,
  apiBaseUrl: 'https://localhost:7167/api',
  /** Ponuda / demo kad je javna registracija isključena — zamijeni svojim brojem prije produkcije. */
  contactSalesPhone: '+387 61 000 000',
  supportContactEmail: 'mojterminrezervacija@gmail.com',
  // Set a DSN per environment to enable Sentry. Empty string keeps the SDK
  // uninitialised (no outbound traffic) — ideal for local dev.
  sentryDsn: '',
  sentryEnvironment: 'development',
  sentryTracesSampleRate: 0
};
