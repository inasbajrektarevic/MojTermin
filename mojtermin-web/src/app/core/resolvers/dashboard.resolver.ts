import { ResolveFn } from '@angular/router';
import { inject } from '@angular/core';
import { ApiService } from '../services/api.service';

export const dashboardResolver: ResolveFn<unknown> = () => {
  const apiService = inject(ApiService);
  return apiService.getDashboardSummary();
};
