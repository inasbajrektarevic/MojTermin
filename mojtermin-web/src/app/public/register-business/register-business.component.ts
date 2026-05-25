import { Component, HostListener, OnDestroy, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ApiService } from '../../core/services/api.service';
import { SiteConfigService } from '../../core/services/site-config.service';
import { ToastService } from '../../core/services/toast.service';
import { HttpErrorResponse } from '@angular/common/http';
import { salesContactPhoneDisplay, salesContactTelHref, hasSalesContactPhone } from '../../core/utils/sales-contact.utils';
import { RegisterBusinessRequest, RegisterBusinessResponse } from '../../shared/models/business.models';
import {
  PhoneCountryOption,
  buildPhoneCountries,
  describePhoneError,
  phoneNumberValidator,
  toInternationalPhone
} from '../../shared/phone/phone.utils';
import { resolveUploadAssetUrl } from '../../core/utils/asset-url.utils';

@Component({
  selector: 'app-register-business',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './register-business.component.html',
  styleUrl: './register-business.component.scss'
})
export class RegisterBusinessComponent implements OnInit, OnDestroy {
  private readonly siteConfig = inject(SiteConfigService);
  private readonly fb = inject(FormBuilder);
  private readonly emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  // Matches the server-side RegionalPhone regex for the final international form.
  private readonly phoneRegex = /^\+\d{6,15}$/;

  // Mirrors UploadsController.MaxFileSizeBytes on the server. We check on the
  // client too so users get instant feedback without burning bandwidth.
  private readonly maxLogoBytes = 5 * 1024 * 1024;
  private readonly acceptedLogoMimeTypes = ['image/jpeg', 'image/png', 'image/webp'];

  loading = false;
  /** null until GET /api/public/site-config resolves. */
  allowPublicReg: boolean | null = null;
  uploadingLogo = false;
  error = '';
  logoPreviewUrl = '';
  private logoObjectUrl: string | null = null;
  /**
   * Set after a successful registration so the template can switch from the
   * form to the "Provjeri email" success card. We keep the response in memory
   * so the resend button has the email at hand without re-asking the user.
   */
  registrationResult: RegisterBusinessResponse | null = null;
  resending = false;
  readonly phoneCountries: PhoneCountryOption[] = buildPhoneCountries();
  countryDropdownOpen = false;
  readonly businessTypeOptions = [
    { value: 1, label: 'Frizerski / beauty salon' },
    { value: 2, label: 'Stomatološka ordinacija' },
    { value: 3, label: 'Autoservis' },
    { value: 4, label: 'Apartman / smještaj' },
    { value: 5, label: 'Fitness / teretana' },
    { value: 6, label: 'Kozmetički salon' },
    { value: 6, label: 'Barber shop' },
    { value: 6, label: 'Masažni / spa centar' },
    { value: 6, label: 'Fizioterapeut' },
    { value: 6, label: 'Privatna ordinacija' },
    { value: 6, label: 'Veterinarska stanica' },
    { value: 6, label: 'Auto praonica' },
    { value: 6, label: 'Servis računara / telefona' },
    { value: 6, label: 'Foto studio' },
    { value: 6, label: 'Edukativni centar / instrukcije' },
    { value: 6, label: 'Konsultantske usluge' },
    { value: 6, label: 'Ostalo' }
  ];

  readonly form = this.fb.group({
    businessName: ['', [Validators.required, Validators.maxLength(150)]],
    slug: ['', [Validators.required, Validators.maxLength(160)]],
    businessType: [1, Validators.required],
    phoneCountryIso2: ['ba', Validators.required],
    phoneNumber: ['', [Validators.required, Validators.maxLength(30)]],
    businessEmail: ['', [Validators.required, Validators.maxLength(120)]],
    address: ['', [Validators.maxLength(250)]],
    description: ['', [Validators.maxLength(1000)]],
    logoUrl: [''],
    ownerFullName: ['', [Validators.required, Validators.maxLength(150)]],
    ownerEmail: ['', [Validators.required, Validators.maxLength(120)]],
    ownerUsername: ['', [Validators.required, Validators.maxLength(80)]],
    ownerPassword: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(100)]]
  }, { validators: phoneNumberValidator('phoneNumber', 'phoneCountryIso2') });

  constructor(
    private readonly authService: AuthService,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

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

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const raw = this.form.getRawValue();
    const businessEmail = (raw.businessEmail ?? '').trim().toLowerCase();
    const ownerEmail = (raw.ownerEmail ?? '').trim().toLowerCase();
    if (!this.emailRegex.test(businessEmail) || !this.emailRegex.test(ownerEmail)) {
      this.error = 'Unesite ispravne email adrese.';
      return;
    }

    const phone = toInternationalPhone(raw.phoneCountryIso2, raw.phoneNumber, this.phoneCountries);
    if (!this.phoneRegex.test(phone)) {
      this.error = 'Unesite ispravan broj telefona.';
      this.toastService.error(this.error);
      return;
    }

    const payload: RegisterBusinessRequest = {
      businessName: (raw.businessName ?? '').trim(),
      slug: this.normalizeSlug(raw.slug ?? ''),
      businessType: Number(raw.businessType ?? 1),
      phone,
      businessEmail,
      address: (raw.address ?? '').trim(),
      description: (raw.description ?? '').trim(),
      logoUrl: (raw.logoUrl ?? '').trim() || undefined,
      ownerFullName: (raw.ownerFullName ?? '').trim(),
      ownerEmail,
      ownerUsername: (raw.ownerUsername ?? '').trim().toLowerCase(),
      ownerPassword: raw.ownerPassword ?? ''
    };

    if (!payload.slug) {
      this.error = 'Slug je obavezan.';
      return;
    }

    this.loading = true;
    this.error = '';
    this.authService.registerBusiness(payload).subscribe({
      next: (response) => {
        this.loading = false;
        // Strict email-verification: no auto-login. Show the "Provjeri email"
        // success card; the user has to click the link in the email before
        // /api/auth/login will issue tokens for this account.
        this.registrationResult = response;
        if (response.emailDispatched) {
          this.toastService.success('Biznis je kreiran. Provjerite email za verifikacioni link.');
        } else if (response.devVerificationUrl) {
          this.toastService.info('Biznis je kreiran. Email nije poslan (SMTP isključen) — koristite dev link na ekranu.');
        } else {
          this.toastService.info('Biznis je kreiran, ali verifikacioni email nije poslan. Pokušajte „Pošalji link ponovo“ ili kontaktirajte podršku.');
        }
      },
      error: (err: HttpErrorResponse) => {
        this.loading = false;
        if (err.status === 403) {
          this.error = 'Javna registracija je isključena. Pozovi broj sa početne stranice za ugovor i aktivaciju naloga.';
          return;
        }
        this.error = 'Registracija nije uspjela. Provjerite podatke i pokušajte ponovo.';
      }
    });
  }

  /** Asks the API to send a fresh verification email (e.g. the first one got lost). */
  resendVerification(): void {
    const email = this.registrationResult?.ownerEmail;
    if (!email || this.resending) {
      return;
    }
    this.resending = true;
    this.authService.resendVerification(email).subscribe({
      next: () => {
        this.resending = false;
        this.toastService.success('Novi verifikacioni link je poslan.');
      },
      error: () => {
        this.resending = false;
        this.toastService.error('Slanje nije uspjelo. Pokušajte ponovo za par minuta.');
      }
    });
  }

  /**
   * Deep-links the owner straight to their email provider's inbox. Mirrors the
   * UX we already use on the booking success page: opens Gmail / Outlook /
   * Yahoo / Apple Mail / a generic mailto handler depending on the domain.
   */
  getInboxLink(): string {
    const email = this.registrationResult?.ownerEmail ?? '';
    const domain = email.split('@')[1]?.toLowerCase() ?? '';
    if (domain.includes('gmail.') || domain.includes('googlemail.')) {
      return 'https://mail.google.com/mail/u/0/#inbox';
    }
    if (domain.includes('outlook.') || domain.includes('hotmail.') || domain.includes('live.') || domain.includes('msn.')) {
      return 'https://outlook.live.com/mail/0/inbox';
    }
    if (domain.includes('yahoo.')) {
      return 'https://mail.yahoo.com/d/folders/1';
    }
    if (domain.includes('icloud.') || domain.includes('me.com') || domain.includes('mac.com')) {
      return 'https://www.icloud.com/mail';
    }
    if (domain.includes('proton.') || domain.includes('protonmail.')) {
      return 'https://mail.proton.me/u/0/inbox';
    }
    if (domain.includes('aol.')) {
      return 'https://mail.aol.com/webmail-std/en-us/suite';
    }
    return `mailto:${email}`;
  }

  ngOnDestroy(): void {
    this.revokeLogoObjectUrl();
  }

  onLogoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    const validationError = this.validateLogoFile(file);
    if (validationError) {
      this.error = validationError;
      this.toastService.error(validationError);
      // Reset the file input so the user can pick the same file again after fixing it.
      input.value = '';
      return;
    }

    this.revokeLogoObjectUrl();
    this.logoObjectUrl = URL.createObjectURL(file);
    this.logoPreviewUrl = this.logoObjectUrl;

    this.uploadingLogo = true;
    this.error = '';
    this.apiService.uploadLogo(file).subscribe({
      next: (response) => {
        this.uploadingLogo = false;
        const resolved = resolveUploadAssetUrl(response.url);
        this.form.patchValue({ logoUrl: resolved });
        this.revokeLogoObjectUrl();
        this.logoPreviewUrl = resolved;
        this.toastService.success('Logo je uspješno uploadovan.');
      },
      error: () => {
        this.uploadingLogo = false;
        this.revokeLogoObjectUrl();
        this.logoPreviewUrl = '';
        this.error = 'Upload loga nije uspio. Pokušajte ponovo.';
      }
    });
  }

  private revokeLogoObjectUrl(): void {
    if (this.logoObjectUrl) {
      URL.revokeObjectURL(this.logoObjectUrl);
      this.logoObjectUrl = null;
    }
  }

  private validateLogoFile(file: File): string | null {
    if (!this.acceptedLogoMimeTypes.includes(file.type)) {
      return 'Odaberite ispravnu sliku formata JPG, PNG ili WEBP.';
    }

    if (file.size <= 0) {
      return 'Odabrani fajl je prazan.';
    }

    if (file.size > this.maxLogoBytes) {
      const sizeMb = (file.size / (1024 * 1024)).toFixed(1);
      const maxMb = (this.maxLogoBytes / (1024 * 1024)).toFixed(0);
      return `Slika je prevelika (${sizeMb} MB). Maksimalna dozvoljena veličina je ${maxMb} MB.`;
    }

    return null;
  }

  private normalizeSlug(input: string): string {
    return input
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9-]/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '');
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

  get selectedPhoneCountry(): PhoneCountryOption | undefined {
    const iso2 = (this.form.get('phoneCountryIso2')?.value ?? 'ba').toLowerCase();
    return this.phoneCountries.find((x) => x.iso2 === iso2);
  }

  /**
   * Builds a Google Maps search URL from the typed address. Lets the owner
   * verify the address resolves to the right pin BEFORE submitting the form
   * — much cheaper than wiring up Google Places autocomplete.
   * Returns null when the field is empty so the UI can disable the button.
   */
  get googleMapsCheckUrl(): string | null {
    const address = (this.form.get('address')?.value ?? '').trim();
    if (!address) {
      return null;
    }
    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    const target = event.target as HTMLElement | null;
    if (!target?.closest('.country-picker')) {
      this.countryDropdownOpen = false;
    }
  }

  phoneFieldError(): string {
    const control = this.form.get('phoneNumber');
    if (!control || !(control.touched || control.dirty) || !control.errors) {
      return '';
    }
    if (control.errors['required']) {
      return 'Polje je obavezno.';
    }
    if (control.errors['maxlength']) {
      return 'Maksimalna dužina je 30 karaktera.';
    }
    const phoneMessage = describePhoneError(control.errors, this.selectedPhoneCountry);
    return phoneMessage ?? 'Neispravan unos.';
  }
}
