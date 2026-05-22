import { Component, OnInit } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { CurrencyPipe, NgStyle } from '@angular/common';
import { ApiService } from '../../core/services/api.service';
import { Business, Service, WorkingHour } from '../../shared/models/business.models';
import { environment } from '../../../environments/environment';

@Component({
  selector: 'app-business-page',
  standalone: true,
  imports: [RouterLink, CurrencyPipe, NgStyle],
  templateUrl: './business-page.component.html',
  styleUrl: './business-page.component.scss'
})
export class BusinessPageComponent implements OnInit {
  private readonly apiBaseUrl = environment.apiBaseUrl;
  coverImageFailed = false;
  // Tracks per-service ids whose primary <img src> failed to load (e.g. a CDN
  // photo got pulled). We swap those to the local SVG fallback on next render.
  private readonly serviceImageFailed = new Set<string>();
  slug = '';
  business: Business | null = null;
  services: Service[] = [];
  workingHours: WorkingHour[] = [];
  readonly dayNames = ['Nedjelja', 'Ponedjeljak', 'Utorak', 'Srijeda', 'Četvrtak', 'Petak', 'Subota'];
  loading = true;
  loadError = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService
  ) {}

  get googleMapsUrl(): string | null {
    const address = this.business?.address?.trim();
    if (!address) {
      return null;
    }

    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
  }

  // The phone is stored as E.164 (e.g. "+38761111222"). For wa.me / tel:
  // links we want digits only; for the visible label we keep the original
  // formatting (with spaces) so it remains human-readable.
  private get phoneDigits(): string {
    return (this.business?.phone ?? '').replace(/\D+/g, '');
  }

  get telUrl(): string | null {
    return this.phoneDigits ? `tel:+${this.phoneDigits}` : null;
  }

  get whatsAppUrl(): string | null {
    if (!this.phoneDigits) {
      return null;
    }
    const greeting = `Pozdrav, zainteresovan/a sam za uslugu salona ${this.business?.name ?? ''}.`.trim();
    return `https://wa.me/${this.phoneDigits}?text=${encodeURIComponent(greeting)}`;
  }

  get sortedWorkingHours(): WorkingHour[] {
    return [...this.workingHours].sort((a, b) => {
      const orderA = a.dayOfWeek === 0 ? 7 : a.dayOfWeek;
      const orderB = b.dayOfWeek === 0 ? 7 : b.dayOfWeek;
      return orderA - orderB;
    });
  }

  /** Today's `Date.getDay()` value (0 = Sunday). Cached per call. */
  private getTodayDayOfWeek(): number {
    return new Date().getDay();
  }

  isToday(dayOfWeek: number): boolean {
    return this.getTodayDayOfWeek() === dayOfWeek;
  }

  /**
   * "09:00:00" or "09:00" → "09:00". We accept both because old DB rows may
   * carry seconds while newer ones don't.
   */
  formatHourTime(value: string | null | undefined): string {
    if (!value) {
      return '';
    }
    // Keep only HH:mm.
    const match = /^(\d{1,2}):(\d{2})/.exec(value);
    if (!match) {
      return value;
    }
    return `${match[1].padStart(2, '0')}:${match[2]}`;
  }

  /** Today's working-hour row, or null if today is missing / closed. */
  private getTodaysHours(): WorkingHour | null {
    const today = this.getTodayDayOfWeek();
    return this.workingHours.find((x) => x.dayOfWeek === today) ?? null;
  }

  /**
   * True when the current local time falls inside today's open window. Does
   * not handle businesses that work past midnight — that pattern is rare for
   * salons and would need a different data shape anyway.
   */
  isOpenNow(): boolean {
    const today = this.getTodaysHours();
    if (!today || today.isClosed) {
      return false;
    }
    const now = new Date();
    const nowMinutes = now.getHours() * 60 + now.getMinutes();
    const open = this.toMinutes(today.openTime);
    const close = this.toMinutes(today.closeTime);
    if (open === null || close === null) {
      return false;
    }
    return nowMinutes >= open && nowMinutes < close;
  }

  /** Human-friendly status line shown next to the section title. */
  getOpenStatusText(): string {
    const today = this.getTodaysHours();
    if (!today || today.isClosed) {
      return 'Danas zatvoreno';
    }
    if (this.isOpenNow()) {
      return `Otvoreno do ${this.formatHourTime(today.closeTime)}`;
    }
    const now = new Date();
    const nowMinutes = now.getHours() * 60 + now.getMinutes();
    const open = this.toMinutes(today.openTime);
    if (open !== null && nowMinutes < open) {
      return `Otvara u ${this.formatHourTime(today.openTime)}`;
    }
    return `Zatvoreno · radi ${this.formatHourTime(today.openTime)} - ${this.formatHourTime(today.closeTime)}`;
  }

  private toMinutes(value: string | null | undefined): number | null {
    if (!value) {
      return null;
    }
    const match = /^(\d{1,2}):(\d{2})/.exec(value);
    if (!match) {
      return null;
    }
    return parseInt(match[1], 10) * 60 + parseInt(match[2], 10);
  }

  ngOnInit(): void {
    this.slug = this.route.snapshot.paramMap.get('slug') ?? '';
    const preload = this.route.snapshot.data['preload'] as
      | { business: Business; services: Service[] }
      | undefined;

    if (preload) {
      this.business = preload.business;
      this.coverImageFailed = false;
      this.services = preload.services;
      this.apiService.getPublicWorkingHours(this.slug).subscribe({
        next: (hours) => {
          this.workingHours = hours;
          this.loading = false;
        },
        error: () => {
          this.loading = false;
          this.loadError = 'Neuspješno učitavanje poslovnog profila.';
        }
      });
    } else {
      let remaining = 3;
      const completeOne = () => {
        remaining -= 1;
        if (remaining <= 0) {
          this.loading = false;
        }
      };

      this.apiService.getBusinessBySlug(this.slug).subscribe({
        next: (business) => {
          this.business = business;
          this.coverImageFailed = false;
          completeOne();
        },
        error: () => {
          this.loadError = 'Neuspješno učitavanje poslovnog profila.';
          completeOne();
        }
      });

      this.apiService.getPublicServices(this.slug).subscribe({
        next: (services) => {
          this.services = services;
          completeOne();
        },
        error: () => {
          this.loadError = 'Neuspješno učitavanje poslovnog profila.';
          completeOne();
        }
      });

      this.apiService.getPublicWorkingHours(this.slug).subscribe({
        next: (hours) => {
          this.workingHours = hours;
          completeOne();
        },
        error: () => {
          this.loadError = 'Neuspješno učitavanje poslovnog profila.';
          completeOne();
        }
      });
    }
  }

  getCoverImageUrl(): string {
    const cover = this.business?.coverImageUrl;
    if (cover && !this.coverImageFailed) {
      return this.toAbsoluteAssetUrl(cover);
    }

    switch (this.business?.businessType) {
      case 1:
        return this.toAbsoluteAssetUrl('/images/covers/beauty-cover.svg');
      case 2:
        return this.toAbsoluteAssetUrl('/images/covers/dental-cover.svg');
      case 3:
        return this.toAbsoluteAssetUrl('/images/covers/car-cover.svg');
      case 4:
        return this.toAbsoluteAssetUrl('/images/covers/apartment-cover.svg');
      case 5:
        return this.toAbsoluteAssetUrl('/images/covers/fitness-cover.svg');
      default:
        return this.toAbsoluteAssetUrl('/images/covers/default-cover.svg');
    }
  }

  onCoverImageError(): void {
    this.coverImageFailed = true;
  }

  shouldShowCoverTitle(): boolean {
    return !this.business?.coverImageUrl || this.coverImageFailed;
  }

  getServiceImageUrl(service: Service): string {
    // If the upstream <img> already failed once, skip the broken URL entirely
    // and serve the keyword-matched local SVG instead so visitors don't see
    // the broken-image placeholder.
    if (service.imageUrl && !this.serviceImageFailed.has(service.id)) {
      return this.toAbsoluteAssetUrl(service.imageUrl);
    }
    return this.getFallbackServiceImageUrl(service);
  }

  onServiceImageError(service: Service): void {
    if (this.serviceImageFailed.has(service.id)) {
      return;
    }
    this.serviceImageFailed.add(service.id);
  }

  private getFallbackServiceImageUrl(service: Service): string {
    const name = service.name.toLowerCase();
    if (name.includes('muško') || name.includes('musko')) {
      return this.toAbsoluteAssetUrl('/images/services/mens-haircut.svg');
    }
    if (name.includes('žensko') || name.includes('zensko')) {
      return this.toAbsoluteAssetUrl('/images/services/womens-haircut.svg');
    }
    if (name.includes('farbanje') || name.includes('bojenje')) {
      return this.toAbsoluteAssetUrl('/images/services/hair-coloring.svg');
    }
    return this.toAbsoluteAssetUrl('/images/services/default-service.svg');
  }

  getThemeStyle(): Record<string, string> {
    const primary = this.business?.primaryColor?.trim() || '#1d4ed8';
    const secondary = this.business?.secondaryColor?.trim() || '#6366f1';
    return {
      '--business-primary': primary,
      '--business-secondary': secondary
    };
  }

  private toAbsoluteAssetUrl(value: string): string {
    if (value.startsWith('http://') || value.startsWith('https://')) {
      return value;
    }
    const normalizedBase = this.apiBaseUrl.endsWith('/') ? this.apiBaseUrl.slice(0, -1) : this.apiBaseUrl;
    const apiOrigin = normalizedBase.endsWith('/api') ? normalizedBase.slice(0, -4) : normalizedBase;
    const normalizedPath = value.startsWith('/') ? value : `/${value}`;
    return `${apiOrigin}${normalizedPath}`;
  }
}
