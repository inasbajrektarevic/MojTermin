import { Component, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { ToastService } from '../../core/services/toast.service';

type FormState = 'idle' | 'submitting' | 'submitted';

@Component({
  selector: 'app-forgot-password',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink],
  templateUrl: './forgot-password.component.html',
  styleUrl: './forgot-password.component.scss'
})
export class ForgotPasswordComponent {
  private readonly authService = inject(AuthService);
  private readonly toastService = inject(ToastService);

  email = '';
  state: FormState = 'idle';
  /** Email captured at submit time so the success card can show which inbox to check. */
  submittedEmail = '';

  submit(): void {
    const trimmed = this.email.trim();
    if (!trimmed || this.state === 'submitting') {
      return;
    }

    // Anti-double-submit: lock the form immediately. We also remember the
    // submitted value so the success copy stays accurate even if the user
    // edits the input afterwards.
    this.state = 'submitting';
    this.submittedEmail = trimmed;

    this.authService.forgotPassword(trimmed).subscribe({
      next: () => {
        this.state = 'submitted';
      },
      error: () => {
        this.state = 'idle';
        this.toastService.error('Slanje nije uspjelo. Pokušajte ponovo za par minuta.');
      }
    });
  }

  /** Builds a deep-link to the user's webmail provider so they can jump to their inbox. */
  get inboxLink(): string {
    const at = this.submittedEmail.indexOf('@');
    if (at < 0) {
      return 'https://mail.google.com';
    }
    const domain = this.submittedEmail.slice(at + 1).toLowerCase();
    if (domain.includes('gmail') || domain.includes('googlemail')) {
      return 'https://mail.google.com';
    }
    if (
      domain.includes('outlook') ||
      domain.includes('hotmail') ||
      domain.includes('live') ||
      domain.includes('msn')
    ) {
      return 'https://outlook.live.com/mail/0/inbox';
    }
    if (domain.includes('yahoo')) {
      return 'https://mail.yahoo.com';
    }
    return `https://${domain}`;
  }
}
