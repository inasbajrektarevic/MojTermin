import { Component, inject } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { switchMap, throwError } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthError } from '../../shared/models/business.models';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss'
})
export class LoginComponent {
  private readonly fb = inject(FormBuilder);

  readonly form = this.fb.group({
    usernameOrEmail: ['', Validators.required],
    password: ['', Validators.required]
  });
  loading = false;
  error = '';
  /**
   * When the API rejects the login with code EMAIL_NOT_VERIFIED we capture the
   * owner email here so the user can trigger a resend without retyping it.
   * Cleared on each new submit attempt.
   */
  unverifiedEmail: string | null = null;
  resending = false;
  readonly slug: string;

  constructor(
    private readonly authService: AuthService,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService,
    private readonly router: Router,
    private readonly route: ActivatedRoute
  ) {
    // Slug is required by the route matcher (path: 'b/:slug/admin/login'),
    // so it should always be present. Fall back to an empty string defensively;
    // submit() will then fail the business lookup and surface an error instead
    // of silently sending the user to someone else's tenant.
    this.slug = this.route.snapshot.paramMap.get('slug') ?? '';
  }

  submit(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    if (!this.slug) {
      this.error = 'Neispravan link za prijavu. Otvorite link koji ste dobili od svog biznisa.';
      return;
    }

    this.loading = true;
    this.error = '';
    this.unverifiedEmail = null;

    const value = this.form.getRawValue();
    this.authService.login((value.usernameOrEmail ?? '').trim(), value.password ?? '').pipe(
      switchMap((auth) => this.apiService.getBusinessBySlug(this.slug).pipe(
        switchMap((business) => {
          if (business.id !== auth.businessId) {
            this.authService.logout();
            return throwError(() => new Error('Prijavljeni korisnik ne pripada ovom biznis panelu.'));
          }

          return this.apiService.getCurrentBusiness();
        })
      ))
    ).subscribe({
      next: (business) => this.router.navigate(['/b', business.slug, 'admin', 'dashboard']),
      error: (err) => {
        this.loading = false;

        // Strict email-verification flow: the API returns 403 + AuthError when
        // the owner has not clicked the verification link yet. We detect that
        // shape here and switch the UI to a resend-CTA instead of a generic
        // "wrong credentials" error.
        if (err instanceof HttpErrorResponse && err.status === 403) {
          const payload = err.error as AuthError | null;
          if (payload?.code === 'EMAIL_NOT_VERIFIED') {
            this.unverifiedEmail = payload.email ?? null;
            this.error = '';
            return;
          }
        }

        this.error = 'Neispravni login podaci ili pogrešan biznis panel.';
      }
    });
  }

  resendVerification(): void {
    if (!this.unverifiedEmail || this.resending) {
      return;
    }
    this.resending = true;
    this.authService.resendVerification(this.unverifiedEmail).subscribe({
      next: () => {
        this.resending = false;
        this.toastService.success('Novi verifikacioni link je poslan. Provjerite email.');
      },
      error: () => {
        this.resending = false;
        this.toastService.error('Slanje nije uspjelo. Pokušajte ponovo za par minuta.');
      }
    });
  }

  fieldError(fieldName: 'usernameOrEmail' | 'password'): string {
    const control = this.form.get(fieldName);
    if (!control || !(control.touched || control.dirty) || !control.errors) {
      return '';
    }

    if (control.errors['required']) {
      return 'Polje je obavezno.';
    }

    return 'Neispravan unos.';
  }
}
