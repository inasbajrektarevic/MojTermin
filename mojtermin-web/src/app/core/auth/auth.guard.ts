import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { ApiService } from '../services/api.service';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (route) => {
  const authService = inject(AuthService);
  const router = inject(Router);
  const apiService = inject(ApiService);
  const slug = route.paramMap.get('slug');

  // If the route has no slug (e.g. someone hand-edited the URL down to /admin)
  // we cannot know which tenant they meant. Send them to the platform home so
  // they can pick / register a tenant instead of silently landing in someone else's.
  if (!slug) {
    return router.createUrlTree(['/']);
  }

  if (!authService.getAccessToken()) {
    return router.createUrlTree(['/b', slug, 'admin', 'login']);
  }

  return apiService.getCurrentBusiness().pipe(
    map((business) => {
      if (business.slug === slug) {
        return true;
      }

      return router.createUrlTree(['/b', business.slug, 'admin', 'dashboard']);
    }),
    catchError(() => {
      authService.logout();
      return of(router.createUrlTree(['/b', slug, 'admin', 'login']));
    })
  );
};
