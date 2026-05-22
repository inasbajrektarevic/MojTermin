import { ResolveFn } from '@angular/router';
import { inject } from '@angular/core';
import { forkJoin } from 'rxjs';
import { ApiService } from '../services/api.service';

export const publicBusinessResolver: ResolveFn<unknown> = (route) => {
  const apiService = inject(ApiService);
  const slug = route.paramMap.get('slug') ?? '';
  return forkJoin({
    business: apiService.getBusinessBySlug(slug),
    services: apiService.getPublicServices(slug)
  });
};
