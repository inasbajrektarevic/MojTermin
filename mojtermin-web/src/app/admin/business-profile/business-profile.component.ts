import { Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthError, Business } from '../../shared/models/business.models';
import { resolveUploadAssetUrl } from '../../core/utils/asset-url.utils';

@Component({
  selector: 'app-business-profile',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './business-profile.component.html',
  styleUrl: './business-profile.component.scss'
})
export class BusinessProfileComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly authService = inject(AuthService);
  private readonly emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  private readonly maxLogoBytes = 5 * 1024 * 1024;
  private readonly acceptedLogoMimeTypes = ['image/jpeg', 'image/png', 'image/webp'];

  @ViewChild('logoFile') private logoFile?: ElementRef<HTMLInputElement>;

  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    slug: ['', [Validators.required, Validators.maxLength(160)]],
    businessType: [6, Validators.required],
    phone: ['', [Validators.required, Validators.maxLength(30)]],
    email: [''],
    address: ['', [Validators.required, Validators.maxLength(250)]],
    description: ['', Validators.maxLength(1000)],
    logoUrl: ['']
  });

  /** Separate FormGroup for the change-password card. Decoupled from the
   *  business form so a half-typed password never blocks profile save. */
  readonly passwordForm = this.fb.group({
    currentPassword: ['', [Validators.required]],
    newPassword: ['', [Validators.required, Validators.minLength(6), Validators.maxLength(200)]],
    confirmPassword: ['', [Validators.required]]
  });

  message = '';
  saving = false;
  uploadingLogo = false;
  logoLoadFailed = false;
  changingPassword = false;
  passwordError = '';
  passwordSuccess = '';
  showCurrentPassword = false;
  showNewPassword = false;
  /** Slug captured from the last server response. Used to build the "Otvori javnu stranicu" deep-link
   * so typing a new slug into the input doesn't break the preview link until Save succeeds. */
  private savedSlug = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

  ngOnInit(): void {
    const business = this.route.snapshot.data['business'] as Business | undefined;
    if (business) {
      this.patchBusinessForm(business);
      this.savedSlug = (business.slug ?? '').trim();
    } else {
      this.apiService.getCurrentBusiness().subscribe((row) => {
        this.patchBusinessForm(row);
        this.savedSlug = (row.slug ?? '').trim();
      });
    }

    this.form.get('logoUrl')?.valueChanges.subscribe(() => {
      this.logoLoadFailed = false;
    });
  }

  get logoPreviewSrc(): string {
    return resolveUploadAssetUrl(this.form.get('logoUrl')?.value);
  }

  /** Builds a link to the public business page using the slug as it was last saved on the server. */
  get publicPageUrl(): string | null {
    const slug = this.savedSlug;
    if (!slug) {
      return null;
    }
    return `/b/${encodeURIComponent(slug)}`;
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.getRawValue();
    const email = (payload.email ?? '').trim();
    if (!email || !this.emailRegex.test(email)) {
      this.toastService.error('Unesite ispravnu email adresu.');
      return;
    }

    const trimmedSlug = (payload.slug ?? '').trim();

    this.saving = true;
    this.apiService.updateBusiness({
      name: (payload.name ?? '').trim(),
      slug: trimmedSlug,
      businessType: payload.businessType ?? 6,
      phone: (payload.phone ?? '').trim(),
      email,
      address: (payload.address ?? '').trim(),
      description: (payload.description ?? '').trim(),
      logoUrl: (payload.logoUrl ?? '').trim() || undefined
    }).subscribe({
      next: () => {
        this.saving = false;
        this.savedSlug = trimmedSlug;
        this.message = 'Profil biznisa je sačuvan.';
        this.toastService.success(this.message);
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  /**
   * Builds a Google Maps search URL from the current address value so owners
   * can verify their address resolves to the right pin. Returns null when the
   * field is empty so the UI can show a disabled state.
   */
  get googleMapsCheckUrl(): string | null {
    const address = (this.form.get('address')?.value ?? '').trim();
    if (!address) {
      return null;
    }
    return `https://www.google.com/maps/search/?api=1&query=${encodeURIComponent(address)}`;
  }

  changePassword(): void {
    this.passwordError = '';
    this.passwordSuccess = '';

    if (this.passwordForm.invalid) {
      this.passwordForm.markAllAsTouched();
      const newPwCtrl = this.passwordForm.get('newPassword');
      if (newPwCtrl?.errors?.['minlength']) {
        this.passwordError = 'Nova lozinka mora imati najmanje 6 karaktera.';
      } else {
        this.passwordError = 'Popunite oba polja sa lozinkom.';
      }
      return;
    }

    const payload = this.passwordForm.getRawValue();
    const currentPassword = (payload.currentPassword ?? '').trim();
    const newPassword = (payload.newPassword ?? '').trim();
    const confirmPassword = (payload.confirmPassword ?? '').trim();

    if (newPassword !== confirmPassword) {
      this.passwordError = 'Lozinke se ne podudaraju.';
      return;
    }
    if (currentPassword === newPassword) {
      this.passwordError = 'Nova lozinka mora biti različita od trenutne.';
      return;
    }

    this.changingPassword = true;
    this.authService.changePassword(currentPassword, newPassword).subscribe({
      next: (response) => {
        this.changingPassword = false;
        this.passwordSuccess = response?.message ?? 'Lozinka je promijenjena.';
        this.passwordForm.reset();
        this.toastService.success(this.passwordSuccess);
      },
      error: (err: HttpErrorResponse) => {
        this.changingPassword = false;
        const payload = err.error as AuthError | null;
        if (payload?.code === 'WRONG_PASSWORD') {
          this.passwordError = 'Trenutna lozinka nije ispravna.';
        } else {
          this.passwordError = payload?.message ?? 'Promjena lozinke nije uspjela. Pokušajte ponovo.';
        }
      }
    });
  }

  toggleCurrentPassword(): void {
    this.showCurrentPassword = !this.showCurrentPassword;
  }

  toggleNewPassword(): void {
    this.showNewPassword = !this.showNewPassword;
  }

  onLogoSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    const err = this.validateLogoFile(file);
    if (err) {
      this.toastService.error(err);
      input.value = '';
      return;
    }

    this.uploadingLogo = true;
    this.apiService.uploadLogo(file).subscribe({
      next: (res) => {
        this.uploadingLogo = false;
        input.value = '';
        this.logoLoadFailed = false;
        const resolved = resolveUploadAssetUrl(res.url);
        this.form.patchValue({ logoUrl: resolved });
        this.toastService.success('Logo je uploadovan.');
      },
      error: () => {
        this.uploadingLogo = false;
        input.value = '';
        this.toastService.error('Upload loga nije uspio.');
      }
    });
  }

  clearLogo(): void {
    this.form.patchValue({ logoUrl: '' });
    this.logoLoadFailed = false;
    this.resetLogoFileInput();
  }

  private resetLogoFileInput(): void {
    const el = this.logoFile?.nativeElement;
    if (el) {
      el.value = '';
    }
  }

  private patchBusinessForm(business: Business): void {
    this.form.patchValue({
      ...business,
      logoUrl: resolveUploadAssetUrl(business.logoUrl) || ''
    });
  }

  private validateLogoFile(file: File): string | null {
    if (!this.acceptedLogoMimeTypes.includes(file.type)) {
      return 'Dozvoljeni su JPG, PNG i WEBP.';
    }
    if (file.size <= 0) {
      return 'Datoteka je prazna.';
    }
    if (file.size > this.maxLogoBytes) {
      return 'Slika je prevelika (maks. 5 MB).';
    }
    return null;
  }

  fieldError(fieldName: 'name' | 'slug' | 'phone' | 'address'): string {
    const control = this.form.get(fieldName);
    if (!control || !(control.touched || control.dirty) || !control.errors) {
      return '';
    }

    if (control.errors['required']) {
      return 'Polje je obavezno.';
    }

    if (control.errors['maxlength']) {
      if (fieldName === 'name') {
        return 'Maksimalna dužina je 150 karaktera.';
      }
      if (fieldName === 'slug') {
        return 'Maksimalna dužina je 160 karaktera.';
      }
      if (fieldName === 'phone') {
        return 'Maksimalna dužina je 30 karaktera.';
      }
      return 'Maksimalna dužina je 250 karaktera.';
    }

    return 'Neispravan unos.';
  }
}
