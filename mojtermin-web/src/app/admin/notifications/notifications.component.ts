import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { NotificationLog, NotificationStatus } from '../../shared/models/business.models';

@Component({
  selector: 'app-notifications',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './notifications.component.html',
  styleUrl: './notifications.component.scss'
})
export class NotificationsComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  notifications: NotificationLog[] = [];
  statusFilter = '';
  fromDate = '';
  toDate = '';
  limit: number | null = null;
  loading = false;
  loadError = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.loadError = '';
    this.apiService.getNotifications({
      status: this.statusFilter ? Number(this.statusFilter) as NotificationStatus : undefined,
      from: this.fromDate || undefined,
      to: this.toDate || undefined,
      limit: this.limit ?? undefined
    }).subscribe({
      next: (rows) => {
        this.notifications = rows;
        this.loading = false;
      },
      error: () => {
        this.loadError = 'Neuspješno učitavanje notifikacija.';
        this.loading = false;
      }
    });
  }

  statusLabel(status: NotificationStatus): string {
    const normalized = String(status);
    switch (normalized) {
      case '2':
      case 'Sent':
        return 'Poslano';
      case '3':
      case 'Failed':
        return 'Neuspješno';
      case '1':
      case 'Skipped':
        return 'Preskočeno';
      default:
        return normalized;
    }
  }

  channelLabel(channel: NotificationLog['channel']): string {
    if (channel === 1 as unknown as NotificationLog['channel']) {
      return 'Email';
    }
    if (channel === 'Email') {
      return 'Email';
    }

    return channel;
  }

  statusClass(status: NotificationStatus): string {
    const normalized = String(status);
    switch (normalized) {
      case '2':
      case 'Sent':
        return 'sent';
      case '3':
      case 'Failed':
        return 'failed';
      case '1':
      case 'Skipped':
        return 'skipped';
      default:
        return '';
    }
  }

  formatCreatedAt(value: string): string {
    const normalized = value.endsWith('Z') ? value : `${value}Z`;
    const parsed = new Date(normalized);
    if (Number.isNaN(parsed.getTime())) {
      return value;
    }
    const dd = parsed.getDate().toString().padStart(2, '0');
    const mm = (parsed.getMonth() + 1).toString().padStart(2, '0');
    const yyyy = parsed.getFullYear();
    const hh = parsed.getHours().toString().padStart(2, '0');
    const min = parsed.getMinutes().toString().padStart(2, '0');
    return `${dd}.${mm}.${yyyy} ${hh}:${min}`;
  }
}
