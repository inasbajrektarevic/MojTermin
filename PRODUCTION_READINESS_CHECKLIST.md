# MojTermin Production Readiness Checklist

This checklist is intended as a final go-live gate for the current codebase state.

## Security

- [x] JWT secret not hardcoded; configured via environment.
- [x] Refresh tokens are hashed in database.
- [x] Email verification required before owner login.
- [x] Forgot/reset password flow implemented with hashed reset tokens.
- [x] Public booking endpoints are rate limited.
- [x] Upload validation includes content-type and basic safety checks.
- [x] Global exception handling and non-leaky API errors in place.

## Booking Reliability

- [x] Slot race condition protected by DB unique filtered indexes.
- [x] Concurrency token (`RowVersion`) present for appointment updates.
- [x] Client self-cancel with tokenized URL implemented.
- [x] Reminder service implemented (24h / 1h).
- [x] Multi-staff base scheduling implemented (per-staff slot uniqueness).
- [x] Staff time-off implemented and enforced in availability + booking.

## Observability & Ops

- [x] Notification logs available in admin.
- [x] Admin audit logs available in admin.
- [x] Sentry backend/frontend wiring added (DSN-gated).
- [x] Docker and compose config present.
- [x] CI pipeline configured (build/test).

## Product Admin Tools

- [x] CSV export for clients.
- [x] CSV export for completed revenue by period.
- [x] Staff CRUD in admin.
- [x] Staff time-off CRUD in admin.

## Email (SMTP + DNS)

- [ ] `NOTIFICATIONS_ENABLED=true` (Docker `.env` ili server env vars).
- [ ] SMTP za **noreply@mojtermin.com** (Resend/SendGrid/M365 — ne Gmail s lažnim From).
- [ ] DNS na **mojtermin.com**: SPF + DKIM (iz provajdera); po želji DMARC.
- [ ] Test: `.\scripts\test-smtp.ps1 -To tvoj@email.com` (lokalno) ili registracija + rezervacija + resend verification.
- [ ] Lokalno: `.\scripts\configure-email.ps1` (Resend ili Gmail).

## Final Manual Go-Live Checks (Required)

- [ ] Set production secrets: `JWT_SECRET`, SMTP creds, DB conn string, Sentry DSN.
- [ ] Confirm `ClientApp__BaseUrl` and CORS allowed origins for real domains.
- [ ] Run DB migration on production database before first deploy.
- [ ] Test core user journey on production-like env:
  - owner registration + verify email
  - owner login
  - add service/client/staff
  - public booking (with and without staff selection)
  - cancel via email link
  - reminder email dispatch
- [ ] Verify alerting (Sentry issue appears for forced test exception).
- [ ] Verify backup/restore procedure for SQL database.

## Current Conclusion

From an engineering implementation perspective, the app is near production-ready and all major P0/P1 and key P2 targets discussed in this thread are implemented. Remaining risk is primarily operational discipline (secrets, deployment configuration, and final manual smoke checks).
