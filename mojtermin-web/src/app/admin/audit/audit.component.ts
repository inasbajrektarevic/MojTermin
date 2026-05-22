import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { AdminAuditLog } from '../../shared/models/business.models';

@Component({
  selector: 'app-audit',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './audit.component.html',
  styleUrl: './audit.component.scss'
})
export class AuditComponent implements OnInit {
  private readonly apiService = inject(ApiService);

  rows: AdminAuditLog[] = [];
  resourceType = '';
  action = '';
  fromDate = '';
  toDate = '';
  limit: number | null = 100;
  loading = false;
  loadError = '';

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading = true;
    this.loadError = '';
    this.apiService.getAdminAuditLogs({
      resourceType: this.resourceType || undefined,
      action: this.action || undefined,
      from: this.fromDate || undefined,
      to: this.toDate || undefined,
      limit: this.limit ?? undefined
    }).subscribe({
      next: (rows) => {
        this.rows = rows;
        this.loading = false;
      },
      error: () => {
        this.loadError = 'Neuspješno učitavanje audit logova.';
        this.loading = false;
      }
    });
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
