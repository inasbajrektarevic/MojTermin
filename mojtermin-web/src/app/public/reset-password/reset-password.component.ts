import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth/auth.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthError } from '../../shared/models/business.models';

type ResetState = 'form' | 'submitting' | 'success' | 'expired' | 'invalid' | 'missing-token';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './reset-password.component.html',
  styleUrl: './reset-password.component.scss'
})
export class ResetPasswordComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly apiService = inject(ApiService);
  private readonly toastService = inject(ToastService);

  state: ResetState = 'form';
  password = '';
  confirmPassword = '';
  showPassword = false;
  /** Inline form error for mismatched passwords or too-short input. */
  validationError = '';
  /** API error message when the token is rejected. */
  serverError = '';

  private token = '';

  ngOnInit(): void {
    this.token = (this.route.snapshot.queryParamMap.get('token') ?? '').trim();
    if (!this.token) {
      this.state = 'missing-token';
    }
  }

  submit(): void {
    this.validationError = '';

    const password = this.password;
    if (!password || password.length < 6) {
      this.validationError = 'Lozinka mora imati najmanje 6 karaktera.';
      return;
    }
    if (password !== this.confirmPassword) {
      this.validationError = 'Lozinke se ne podudaraju.';
      return;
    }
    if (this.state === 'submitting') {
      return;
    }

    this.state = 'submitting';
    this.authService.resetPassword(this.token, password).subscribe({
      next: () => {
        this.state = 'success';
        this.toastService.success('Lozinka je promijenjena. Prijava u toku...');
        // Tokens are already stored by AuthService.resetPassword; resolve the
        // current business so we know which tenant URL to land on.
        this.apiService.getCurrentBusiness().subscribe({
          next: (business) => {
            this.router.navigate(['/b', business.slug, 'admin', 'dashboard']);
          },
          error: () => {
            this.router.navigate(['/']);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        const payload = err.error as AuthError | null;
        if (payload?.code === 'TOKEN_EXPIRED') {
          this.state = 'expired';
        } else if (payload?.code === 'TOKEN_INVALID' || payload?.code === 'TOKEN_MISSING') {
          this.state = 'invalid';
        } else {
          this.state = 'invalid';
          this.serverError = payload?.message || 'Greška pri resetu lozinke. Pokušajte ponovo.';
        }
      }
    });
  }

  togglePassword(): void {
    this.showPassword = !this.showPassword;
  }

  /** Helper for the template — using the field directly inside `@case ('form')`
   *  triggers Angular's strict type-narrowing because `state` was just compared
   *  to 'form' on the parent @case. A method returns a fresh boolean and
   *  avoids the narrowed-type "no overlap" error. */
  isSubmitting(): boolean {
    return this.state === 'submitting';
  }
}
