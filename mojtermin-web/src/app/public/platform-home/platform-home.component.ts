import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { SiteConfigService } from '../../core/services/site-config.service';
import { ToastService } from '../../core/services/toast.service';
import { salesContactPhoneDisplay, salesContactTelHref, hasSalesContactPhone } from '../../core/utils/sales-contact.utils';

@Component({
  selector: 'app-platform-home',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule],
  templateUrl: './platform-home.component.html',
  styleUrl: './platform-home.component.scss'
})
export class PlatformHomeComponent implements OnInit {
  private readonly router = inject(Router);
  private readonly apiService = inject(ApiService);
  private readonly siteConfig = inject(SiteConfigService);
  private readonly toastService = inject(ToastService);

  // Public demo tenant is seeded on the API (dev + production) for marketing.
  readonly showDemoLink = true;
  /** Mirrors GET /api/public/site-config; null until the first response. */
  allowPublicReg: boolean | null = null;
  slug = '';
  loading = false;

  ngOnInit(): void {
    this.siteConfig.getPublicSiteConfig().subscribe((cfg) => {
      this.allowPublicReg = cfg.allowPublicRegistration;
    });
  }

  get salesTelHref(): string {
    return salesContactTelHref();
  }

  get salesPhoneDisplay(): string {
    return salesContactPhoneDisplay();
  }

  get hasSalesPhone(): boolean {
    return hasSalesContactPhone();
  }

  goToBusiness(): void {
    const normalized = this.normalizeSlug(this.slug);
    if (!normalized) {
      this.toastService.error('Unesite ispravan slug biznisa.');
      return;
    }

    this.resolveSlugAndNavigate(normalized, 'public');
  }

  goToAdminLogin(): void {
    const normalized = this.normalizeSlug(this.slug);
    if (!normalized) {
      this.toastService.error('Unesite ispravan slug biznisa.');
      return;
    }

    this.resolveSlugAndNavigate(normalized, 'admin');
  }

  private resolveSlugAndNavigate(slug: string, target: 'public' | 'admin'): void {
    if (this.loading) {
      return;
    }

    this.loading = true;
    this.apiService.getBusinessBySlug(slug).subscribe({
      next: (business) => {
        this.loading = false;
        if (target === 'admin') {
          this.router.navigate(['/b', business.slug, 'admin', 'login']);
          return;
        }

        this.router.navigate(['/b', business.slug]);
      },
      error: () => {
        this.loading = false;
        this.toastService.error('Biznis sa unesenim slugom nije pronađen.');
      }
    });
  }

  private normalizeSlug(input: string): string {
    return input
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9-]/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '');
  }
}
