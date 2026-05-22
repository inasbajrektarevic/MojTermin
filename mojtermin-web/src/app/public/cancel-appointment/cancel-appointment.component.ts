import { Component, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';
import { PublicAppointmentSummary } from '../../shared/models/business.models';

type CancelState =
  | 'loading'
  | 'confirm'
  | 'cancelling'
  | 'cancelled'
  | 'already-cancelled'
  | 'too-late'
  | 'invalid'
  | 'missing-token';

@Component({
  selector: 'app-cancel-appointment',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './cancel-appointment.component.html',
  styleUrl: './cancel-appointment.component.scss'
})
export class CancelAppointmentComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly apiService = inject(ApiService);
  private readonly toastService = inject(ToastService);

  state: CancelState = 'loading';
  summary: PublicAppointmentSummary | null = null;

  private token = '';

  ngOnInit(): void {
    this.token = (this.route.snapshot.queryParamMap.get('token') ?? '').trim();
    if (!this.token) {
      this.state = 'missing-token';
      return;
    }

    // Preview only (read-only) — the actual cancel happens on user confirmation.
    this.apiService.lookupCancelAppointment(this.token).subscribe({
      next: (summary) => {
        this.summary = summary;
        if (summary.alreadyCancelled) {
          this.state = 'already-cancelled';
        } else if (summary.tooLateToCancel) {
          this.state = 'too-late';
        } else {
          this.state = 'confirm';
        }
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 404) {
          this.state = 'invalid';
        } else {
          this.state = 'invalid';
        }
      }
    });
  }

  confirmCancel(): void {
    if (this.state === 'cancelling') {
      return;
    }
    this.state = 'cancelling';

    this.apiService.cancelAppointmentByToken(this.token).subscribe({
      next: (summary) => {
        this.summary = summary;
        if (summary.tooLateToCancel) {
          this.state = 'too-late';
        } else {
          this.state = 'cancelled';
          this.toastService.success('Termin je otkazan.');
        }
      },
      error: (err: HttpErrorResponse) => {
        if (err.status === 404) {
          this.state = 'invalid';
        } else {
          this.state = 'invalid';
          this.toastService.error('Otkazivanje nije uspjelo. Pokušaj ponovo ili kontaktiraj salon.');
        }
      }
    });
  }

  formatTime(time: string): string {
    // The API returns TimeSpan as "HH:mm:ss"; trim seconds for display.
    if (!time) {
      return '';
    }
    return time.slice(0, 5);
  }

  formatDate(dateString: string): string {
    if (!dateString) {
      return '';
    }
    try {
      return new Date(dateString).toLocaleDateString('bs-BA', {
        weekday: 'long',
        day: 'numeric',
        month: 'long',
        year: 'numeric'
      });
    } catch {
      return dateString;
    }
  }
}
