import { Component, OnInit } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { Appointment, AppointmentStatus } from '../../shared/models/business.models';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-appointments',
  standalone: true,
  imports: [DatePipe, FormsModule],
  templateUrl: './appointments.component.html',
  styleUrl: './appointments.component.scss'
})
export class AppointmentsComponent implements OnInit {
  appointments: Appointment[] = [];
  statusLoadingId: string | null = null;
  deletingId: string | null = null;
  query = '';
  statusFilter = '';
  loading = false;
  loadError = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

  ngOnInit(): void {
    this.appointments = (this.route.snapshot.data['appointments'] as Appointment[] | undefined) ?? [];
    if (!this.appointments.length) {
      this.load();
    }
  }

  setStatus(appointmentId: string, action: 'cancel'): void {
    this.statusLoadingId = appointmentId;
    this.apiService.updateAppointmentStatus(appointmentId, action).subscribe({
      next: () => {
        this.load();
        this.statusLoadingId = null;
        this.toastService.success('Status termina je ažuriran.');
      },
      error: () => {
        this.statusLoadingId = null;
      }
    });
  }

  remove(appointmentId: string): void {
    if (!confirm('Da li ste sigurni da želite obrisati termin?')) {
      return;
    }

    this.deletingId = appointmentId;
    this.apiService.deleteAppointment(appointmentId).subscribe({
      next: () => {
        this.deletingId = null;
        this.load();
        this.toastService.success('Termin je obrisan.');
      },
      error: () => {
        this.deletingId = null;
      }
    });
  }

  get filteredAppointments(): Appointment[] {
    const query = this.query.trim().toLowerCase();
    return this.appointments.filter((x) => {
      const normalizedStatus = this.normalizeStatus(x.status);
      const statusOk = !this.statusFilter || normalizedStatus === this.statusFilter;
      if (!statusOk) {
        return false;
      }
      if (!query) {
        return true;
      }
      return (
        x.clientName.toLowerCase().includes(query) ||
        x.serviceName.toLowerCase().includes(query) ||
        normalizedStatus.toLowerCase().includes(query)
      );
    });
  }

  statusLabel(status: AppointmentStatus): string {
    switch (this.normalizeStatus(status)) {
      case 'Pending':
        return 'Na cekanju';
      case 'Confirmed':
        return 'Potvrdjen';
      case 'Rejected':
        return 'Odbijen';
      case 'Cancelled':
        return 'Otkazan';
      case 'Completed':
        return 'Zavrsen';
      default:
        return 'Nepoznato';
    }
  }

  statusClass(status: AppointmentStatus): string {
    switch (this.normalizeStatus(status)) {
      case 'Pending':
        return 'pending';
      case 'Confirmed':
        return 'confirmed';
      case 'Rejected':
        return 'rejected';
      case 'Cancelled':
        return 'cancelled';
      case 'Completed':
        return 'completed';
      default:
        return '';
    }
  }

  canApplyAction(status: AppointmentStatus, action: 'cancel'): boolean {
    const normalized = this.normalizeStatus(status);
    switch (action) {
      case 'cancel':
        return normalized === 'Pending' || normalized === 'Confirmed';
      default:
        return false;
    }
  }

  private normalizeStatus(status: AppointmentStatus): 'Pending' | 'Confirmed' | 'Rejected' | 'Cancelled' | 'Completed' {
    if (typeof status === 'number') {
      return this.mapNumericStatus(status);
    }
    return status as 'Pending' | 'Confirmed' | 'Rejected' | 'Cancelled' | 'Completed';
  }

  private mapNumericStatus(status: number): 'Pending' | 'Confirmed' | 'Rejected' | 'Cancelled' | 'Completed' {
    switch (status) {
      case 1:
        return 'Pending';
      case 2:
        return 'Confirmed';
      case 3:
        return 'Cancelled';
      case 4:
        return 'Completed';
      case 5:
        return 'Rejected';
      default:
        return 'Pending';
    }
  }

  private load(): void {
    this.loading = true;
    this.loadError = '';
    this.apiService.getAppointments().subscribe({
      next: (rows) => {
        this.appointments = rows;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.loadError = 'Neuspješno učitavanje termina. Pokušajte ponovo.';
      }
    });
  }
}
