import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, shareReplay } from 'rxjs';
import { environment } from '../../../environments/environment';

export interface PublicSiteConfig {
  allowPublicRegistration: boolean;
}

@Injectable({ providedIn: 'root' })
export class SiteConfigService {
  private readonly http = inject(HttpClient);
  /**
   * Cached so the home page and /register-business share one request.
   * On failure, fail closed (no public registration UI) so we do not advertise a form that returns 403.
   */
  private readonly config$: Observable<PublicSiteConfig> = this.http
    .get<PublicSiteConfig>(`${environment.apiBaseUrl}/public/site-config`)
    .pipe(
      catchError(() => of({ allowPublicRegistration: false })),
      shareReplay(1)
    );

  getPublicSiteConfig(): Observable<PublicSiteConfig> {
    return this.config$;
  }
}
