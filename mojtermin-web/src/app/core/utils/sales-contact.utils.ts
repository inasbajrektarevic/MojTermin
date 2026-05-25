import { environment } from '../../../environments/environment';

/** Broj iz environmenta, formatiran za prikaz (npr. +387 61 123 456). */
export function salesContactPhoneDisplay(): string {
  return (environment.contactSalesPhone ?? '').trim();
}

/** Normalizovan `tel:` URI (samo + i cifre). */
export function salesContactTelHref(): string {
  const cleaned = salesContactPhoneDisplay().replace(/[^\d+]/g, '');
  if (!cleaned) {
    return '#';
  }
  return cleaned.startsWith('+') ? `tel:${cleaned}` : `tel:+${cleaned}`;
}

/** Email za demo podršku / kontakt na početnoj. */
export function supportContactEmailDisplay(): string {
  return (environment.supportContactEmail ?? '').trim();
}

export function supportContactMailtoHref(): string {
  const email = supportContactEmailDisplay();
  return email ? `mailto:${email}` : '#';
}

/** True when a real sales phone is configured (not empty / placeholder). */
export function hasSalesContactPhone(): boolean {
  const raw = salesContactPhoneDisplay();
  if (!raw) {
    return false;
  }
  return raw.replace(/\s/g, '') !== '+38761000000';
}
