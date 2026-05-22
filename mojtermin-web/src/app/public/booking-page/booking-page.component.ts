import { Component, HostListener, OnInit, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { Business, PublicAppointmentSlot, Service, StaffMember } from '../../shared/models/business.models';
import { ToastService } from '../../core/services/toast.service';
import {
  PhoneCountryOption,
  buildPhoneCountries,
  describePhoneError,
  phoneNumberValidator,
  toInternationalPhone
} from '../../shared/phone/phone.utils';

@Component({
  selector: 'app-booking-page',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './booking-page.component.html',
  styleUrl: './booking-page.component.scss'
})
export class BookingPageComponent implements OnInit {
  private readonly fb = inject(FormBuilder);

  slug = '';
  services: Service[] = [];
  staffMembers: StaffMember[] = [];
  business: Business | null = null;
  /**
   * Snapshot of the successful booking, used by the confirmation card and the
   * "Open my inbox" deep link. Null while the form is still active.
   */
  confirmation: {
    service: Service;
    date: string;
    time: string;
    fullName: string;
    email: string;
  } | null = null;
  submitting = false;
  loading = false;
  loadingAvailability = false;
  loadError = '';
  availabilityError = '';
  slots: Array<PublicAppointmentSlot & { display: string; reasonLabel: string }> = [];
  private readonly emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  private readonly phoneRegex = /^\+\d{6,15}$/;
  readonly phoneCountries: PhoneCountryOption[] = buildPhoneCountries();
  countryDropdownOpen = false;

  readonly form = this.fb.group({
    serviceId: ['', Validators.required],
    staffMemberId: [''],
    appointmentDate: ['', Validators.required],
    startTime: ['', Validators.required],
    fullName: ['', [Validators.required, Validators.maxLength(150)]],
    phoneCountryIso2: ['ba', Validators.required],
    phoneNumber: ['', [Validators.required, Validators.maxLength(30)]],
    email: ['', [Validators.required]],
    note: [''],
    // Honeypot — hidden from humans via CSS, populated by naive bots. Server
    // rejects any non-empty value. Kept as a form control (instead of a raw
    // input) so it round-trips through reset() like the other fields.
    website: ['']
  }, { validators: phoneNumberValidator() });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

  ngOnInit(): void {
    this.slug = this.route.snapshot.paramMap.get('slug') ?? '';
    // Optional deep-link from business page service cards: /b/:slug/book?serviceId=...
    const preselectedServiceId = this.route.snapshot.queryParamMap.get('serviceId') ?? '';
    const preload = this.route.snapshot.data['preload'] as
      | { business: Business; services: Service[] }
      | undefined;

    if (preload?.services?.length) {
      this.business = preload.business;
      this.services = preload.services;
      this.applyPreselectedService(preselectedServiceId);
      this.refreshAvailability();
      return;
    }

    this.loading = true;
    this.apiService.getPublicServices(this.slug).subscribe({
      next: (services) => {
        this.services = services;
        this.loading = false;
        this.applyPreselectedService(preselectedServiceId);
        this.refreshAvailability();
      },
      error: () => {
        this.loading = false;
        this.loadError = 'Neuspješno učitavanje usluga.';
      }
    });
    this.apiService.getPublicStaff(this.slug).subscribe({
      next: (rows) => {
        this.staffMembers = rows;
        // Auto-select the single active staff member to reduce clicks.
        if (rows.length === 1) {
          this.form.patchValue({ staffMemberId: rows[0].id }, { emitEvent: false });
        }
        this.refreshAvailability();
      },
      error: () => {
        this.staffMembers = [];
      }
    });
    // Business is needed for the WhatsApp confirmation deep-link (phone + name).
    // Fetched in parallel; failure here is silent because it does not block booking.
    this.apiService.getBusinessBySlug(this.slug).subscribe({
      next: (business) => {
        this.business = business;
      },
      error: () => {
        // Booking can still go through without business info; the WhatsApp
        // button will just not be rendered.
      }
    });
  }

  private applyPreselectedService(serviceId: string): void {
    if (!serviceId) {
      return;
    }
    const match = this.services.find((x) => x.id === serviceId);
    if (match) {
      this.form.patchValue({ serviceId: match.id });
    }
  }

  onAvailabilityInputChanged(): void {
    this.refreshAvailability();
  }

  toggleCountryDropdown(event: MouseEvent): void {
    event.stopPropagation();
    this.countryDropdownOpen = !this.countryDropdownOpen;
  }

  selectCountry(iso2: string): void {
    this.form.patchValue({ phoneCountryIso2: iso2 }, { emitEvent: false });
    this.countryDropdownOpen = false;
    // Re-run the cross-field phone validator so the error message refreshes
    // when the user picks a different country with different length rules.
    this.form.updateValueAndValidity({ emitEvent: false });
    const phone = this.form.get('phoneNumber');
    if (phone?.touched || phone?.dirty) {
      phone.markAsTouched();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    if (!target?.closest('.country-picker')) {
      this.countryDropdownOpen = false;
    }
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload = this.form.getRawValue();
    const email = (payload.email ?? '').trim();
    const note = (payload.note ?? '').trim();
    if (!email) {
      this.toastService.error('Email je obavezan.');
      return;
    }

    if (!this.emailRegex.test(email)) {
      this.toastService.error('Unesite ispravnu email adresu.');
      return;
    }

    const phone = this.getInternationalPhone();
    if (!this.phoneRegex.test(phone)) {
      this.toastService.error('Unesite ispravan međunarodni broj telefona.');
      return;
    }

    const normalizedDate = this.normalizeDate(payload.appointmentDate ?? '');
    const normalizedTime = this.normalizeTime(payload.startTime ?? '');
    if (!normalizedDate || !normalizedTime) {
      this.toastService.error('Molimo unesite ispravan datum i vrijeme termina.');
      return;
    }

    const fullName = (payload.fullName ?? '').trim();
    const selectedService = this.services.find((x) => x.id === payload.serviceId);

    this.submitting = true;
    this.apiService.bookAppointmentPublic(this.slug, {
      serviceId: payload.serviceId ?? '',
      staffMemberId: (payload.staffMemberId ?? '').trim() || undefined,
      appointmentDate: normalizedDate,
      startTime: normalizedTime,
      fullName,
      phone,
      email,
      note: note || undefined,
      website: payload.website ?? ''
    }).subscribe({
      next: () => {
        this.submitting = false;
        if (selectedService) {
          this.confirmation = {
            service: selectedService,
            date: normalizedDate,
            time: normalizedTime,
            fullName,
            email
          };
        }
        this.refreshAvailability();
        this.toastService.success('Termin je uspjesno rezervisan.');
      },
      error: () => {
        this.submitting = false;
      }
    });
  }

  /**
   * Map the customer's email domain to their provider's web inbox so the
   * "Otvori moj email" CTA jumps them straight to the message. Unknown
   * providers return null — the UI falls back to a generic hint.
   */
  getInboxLink(): { url: string; label: string; brand: 'gmail' | 'outlook' | 'yahoo' | 'icloud' | 'proton' | 'aol' | 'generic' } | null {
    const email = this.confirmation?.email?.toLowerCase().trim() ?? '';
    const at = email.lastIndexOf('@');
    if (at < 0) {
      return null;
    }
    const domain = email.slice(at + 1);

    if (/^(gmail|googlemail)\.com$/.test(domain)) {
      return { url: 'https://mail.google.com/mail/u/0/#inbox', label: 'Otvori Gmail', brand: 'gmail' };
    }
    if (/^(hotmail|outlook|live|msn)\.com$/.test(domain) || /^outlook\./.test(domain)) {
      return { url: 'https://outlook.live.com/mail/0/inbox', label: 'Otvori Outlook', brand: 'outlook' };
    }
    if (/^(yahoo|ymail|rocketmail)\.[a-z.]+$/.test(domain)) {
      return { url: 'https://mail.yahoo.com', label: 'Otvori Yahoo Mail', brand: 'yahoo' };
    }
    if (/^(icloud|me|mac)\.com$/.test(domain)) {
      return { url: 'https://www.icloud.com/mail', label: 'Otvori iCloud Mail', brand: 'icloud' };
    }
    if (/^proton(mail)?\.(com|me|ch)$/.test(domain)) {
      return { url: 'https://mail.proton.me/inbox', label: 'Otvori Proton Mail', brand: 'proton' };
    }
    if (domain === 'aol.com') {
      return { url: 'https://mail.aol.com', label: 'Otvori AOL Mail', brand: 'aol' };
    }
    return null;
  }

  /** Reset the form back to its initial state so the user can book again. */
  resetForNewBooking(): void {
    this.confirmation = null;
    this.form.reset({
      serviceId: '',
      staffMemberId: '',
      appointmentDate: '',
      startTime: '',
      fullName: '',
      phoneCountryIso2: 'ba',
      phoneNumber: '',
      email: '',
      note: '',
      website: ''
    });
    this.refreshAvailability();
  }

  /** "2026-05-12" → "ponedjeljak, 12.05.2026." (bs locale). */
  formatDateLong(yyyyMmDd: string): string {
    const parts = /^(\d{4})-(\d{2})-(\d{2})$/.exec(yyyyMmDd);
    if (!parts) {
      return yyyyMmDd;
    }
    const date = new Date(Number(parts[1]), Number(parts[2]) - 1, Number(parts[3]));
    if (Number.isNaN(date.getTime())) {
      return yyyyMmDd;
    }
    const dayNames = ['nedjelja', 'ponedjeljak', 'utorak', 'srijeda', 'četvrtak', 'petak', 'subota'];
    return `${dayNames[date.getDay()]}, ${parts[3]}.${parts[2]}.${parts[1]}.`;
  }

  private normalizeDate(input: string): string | null {
    const trimmed = input.trim();
    if (!trimmed) {
      return null;
    }

    // Native date input usually returns yyyy-MM-dd.
    if (/^\d{4}-\d{2}-\d{2}$/.test(trimmed)) {
      return trimmed;
    }

    // Fallback for locale formatted values like MM/DD/YYYY.
    const parsed = new Date(trimmed);
    if (Number.isNaN(parsed.getTime())) {
      return null;
    }

    const y = parsed.getFullYear();
    const m = String(parsed.getMonth() + 1).padStart(2, '0');
    const d = String(parsed.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }

  private normalizeTime(input: string): string | null {
    const trimmed = input.trim();
    if (!trimmed) {
      return null;
    }

    // 24h format HH:mm
    const hhmm = trimmed.match(/^(\d{1,2}):(\d{2})$/);
    if (hhmm) {
      const hours = Number(hhmm[1]);
      const minutes = Number(hhmm[2]);
      if (hours >= 0 && hours <= 23 && minutes >= 0 && minutes <= 59) {
        return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`;
      }
    }

    // 12h format h:mm AM/PM
    const ampm = trimmed.match(/^(\d{1,2}):(\d{2})\s?(AM|PM)$/i);
    if (ampm) {
      let hours = Number(ampm[1]);
      const minutes = Number(ampm[2]);
      const period = ampm[3].toUpperCase();
      if (minutes < 0 || minutes > 59 || hours < 1 || hours > 12) {
        return null;
      }
      if (period === 'PM' && hours !== 12) {
        hours += 12;
      }
      if (period === 'AM' && hours === 12) {
        hours = 0;
      }
      return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:00`;
    }

    return null;
  }

  private refreshAvailability(): void {
    const serviceId = (this.form.get('serviceId')?.value ?? '').trim();
    const staffMemberId = (this.form.get('staffMemberId')?.value ?? '').trim();
    const date = this.normalizeDate(this.form.get('appointmentDate')?.value ?? '');
    this.availabilityError = '';

    if (!serviceId || !date) {
      this.slots = [];
      this.form.patchValue({ startTime: '' }, { emitEvent: false });
      return;
    }

    this.loadingAvailability = true;
    this.apiService.getPublicAvailability(this.slug, serviceId, date, staffMemberId || undefined).subscribe({
      next: (response) => {
        this.loadingAvailability = false;
        this.slots = (response.slots ?? [])
          .map((x) => {
            const display = this.toHourMinute(x.startTime);
            if (!display) {
              return null;
            }

            return {
              ...x,
              display,
              reasonLabel: this.reasonLabel(x.unavailableReason)
            };
          })
          .filter((x): x is PublicAppointmentSlot & { display: string; reasonLabel: string } => !!x);

        const selectedTime = (this.form.get('startTime')?.value ?? '').trim();
        const selectedExistsAndAvailable = this.slots.some((x) => x.display == selectedTime && x.isAvailable);
        if (selectedTime && !selectedExistsAndAvailable) {
          this.form.patchValue({ startTime: '' }, { emitEvent: false });
        }
      },
      error: () => {
        this.loadingAvailability = false;
        this.slots = [];
        this.form.patchValue({ startTime: '' }, { emitEvent: false });
        this.availabilityError = 'Neuspješno učitavanje slobodnih termina.';
      }
    });
  }

  private toHourMinute(time: string): string | null {
    const trimmed = (time ?? '').trim();
    const hhmmss = trimmed.match(/^(\d{2}):(\d{2})(:\d{2})?$/);
    if (!hhmmss) {
      return null;
    }

    return `${hhmmss[1]}:${hhmmss[2]}`;
  }

  private reasonLabel(reason: PublicAppointmentSlot['unavailableReason']): string {
    if (reason === 'Past') {
      return 'prošlo';
    }

    if (reason === 'Booked') {
      return 'zauzeto';
    }

    return 'nedostupno';
  }

  private getInternationalPhone(): string {
    return toInternationalPhone(
      this.form.get('phoneCountryIso2')?.value,
      this.form.get('phoneNumber')?.value,
      this.phoneCountries
    );
  }

  get selectedPhoneCountry(): PhoneCountryOption | undefined {
    const iso2 = (this.form.get('phoneCountryIso2')?.value ?? 'ba').toLowerCase();
    return this.phoneCountries.find((x) => x.iso2 === iso2);
  }

  fieldError(fieldName: 'serviceId' | 'appointmentDate' | 'startTime' | 'fullName' | 'phoneNumber' | 'email'): string {
    const control = this.form.get(fieldName);
    if (!control || !(control.touched || control.dirty) || !control.errors) {
      return '';
    }

    if (control.errors['required']) {
      return 'Polje je obavezno.';
    }

    if (control.errors['maxlength']) {
      return fieldName === 'fullName'
        ? 'Maksimalna dužina je 150 karaktera.'
        : 'Maksimalna dužina je 30 karaktera.';
    }

    if (fieldName === 'phoneNumber') {
      const phoneMessage = describePhoneError(control.errors, this.selectedPhoneCountry);
      if (phoneMessage) {
        return phoneMessage;
      }
      if (control.errors['pattern']) {
        return 'Unesite ispravan broj telefona.';
      }
    }

    return 'Neispravan unos.';
  }
}
