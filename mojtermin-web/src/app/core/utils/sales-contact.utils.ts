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
