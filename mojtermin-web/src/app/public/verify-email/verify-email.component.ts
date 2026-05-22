import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../core/auth/auth.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { AuthError } from '../../shared/models/business.models';

type VerifyState = 'pending' | 'success' | 'expired' | 'invalid' | 'missing-token';

@Component({
  selector: 'app-verify-email',
  standalone: true,
  imports: [CommonModule, RouterLink, FormsModule],
  templateUrl: './verify-email.component.html',
  styleUrl: './verify-email.component.scss'
})
export class VerifyEmailComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly authService = inject(AuthService);
  private readonly apiService = inject(ApiService);
  private readonly toastService = inject(ToastService);

  state: VerifyState = 'pending';
  /** Filled from the API error payload so the resend button can prefill an email. */
  recoveryEmail: string | null = null;
  /** Inline form value when the API didn't echo back an email (e.g. invalid token). */
  manualEmail = '';
  resending = false;

  ngOnInit(): void {
    const token = (this.route.snapshot.queryParamMap.get('token') ?? '').trim();
    if (!token) {
      this.state = 'missing-token';
      return;
    }

    this.authService.verifyEmail(token).subscribe({
      next: () => {
        this.state = 'success';
        this.toastService.success('Email je verifikovan. Prijava u toku...');
        // The API already stored tokens via AuthService.verifyEmail. Now resolve
        // the freshly-attached business so we know which tenant URL to land on.
        this.apiService.getCurrentBusiness().subscribe({
          next: (business) => {
            this.router.navigate(['/b', business.slug, 'admin', 'dashboard']);
          },
          error: () => {
            // Fallback: dashboard slug is unknown but tokens exist; bounce to
            // home so the user can at least pick the right tenant manually.
            this.router.navigate(['/']);
          }
        });
      },
      error: (err: HttpErrorResponse) => {
        const payload = err.error as AuthError | null;
        if (payload?.code === 'TOKEN_EXPIRED') {
          this.state = 'expired';
          this.recoveryEmail = payload.email ?? null;
        } else {
          this.state = 'invalid';
          this.recoveryEmail = payload?.email ?? null;
        }
      }
    });
  }

  resend(): void {
    const email = (this.recoveryEmail || this.manualEmail).trim();
    if (!email || this.resending) {
      return;
    }
    this.resending = true;
    this.authService.resendVerification(email).subscribe({
      next: () => {
        this.resending = false;
        this.toastService.success('Ako email postoji, novi verifikacioni link je poslan.');
      },
      error: () => {
        this.resending = false;
        this.toastService.error('Slanje nije uspjelo. Pokušajte ponovo za par minuta.');
      }
    });
  }
}
