import { environment } from '../../../environments/environment';

/** Origin of the API host (no `/api` suffix) for static uploads under `/uploads/…`. */
export function apiAssetOrigin(): string {
  return environment.apiBaseUrl.replace(/\/api\/?$/i, '');
}

/**
 * Turns upload API responses into a browser-loadable absolute URL.
 * Uploads are served from the API origin, not the Angular app origin.
 */
export function resolveUploadAssetUrl(url: string | null | undefined): string {
  const trimmed = (url ?? '').trim();
  if (!trimmed) {
    return '';
  }
  if (/^(https?:|data:|blob:)/i.test(trimmed)) {
    return trimmed;
  }
  const origin = apiAssetOrigin();
  return `${origin}${trimmed.startsWith('/') ? trimmed : `/${trimmed}`}`;
}
