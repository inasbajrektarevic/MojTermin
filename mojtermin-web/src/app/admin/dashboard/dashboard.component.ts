import { Component, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { DashboardSummary } from '../../shared/models/business.models';

@Component({
  selector: 'app-dashboard',
  standalone: true,
  imports: [FormsModule],
  templateUrl: './dashboard.component.html',
  styleUrl: './dashboard.component.scss'
})
export class DashboardComponent implements OnInit {
  summary: DashboardSummary | null = null;
  exportFrom = '';
  exportTo = '';
  exporting = false;

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService
  ) {}

  ngOnInit(): void {
    const now = new Date();
    this.exportTo = this.toDateInput(now);
    const from = new Date(now);
    from.setDate(from.getDate() - 30);
    this.exportFrom = this.toDateInput(from);

    this.summary = (this.route.snapshot.data['summary'] as DashboardSummary | undefined) ?? null;
    if (!this.summary) {
      this.apiService.getDashboardSummary().subscribe((summary) => (this.summary = summary));
    }
  }

  exportRevenueCsv(): void {
    if (this.exporting) {
      return;
    }
    this.exporting = true;
    this.apiService.exportRevenueCsv(this.exportFrom, this.exportTo).subscribe({
      next: (blob) => {
        this.exporting = false;
        const fileName = `prihod-${this.exportFrom || 'od'}-${this.exportTo || 'do'}.csv`;
        const url = window.URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = fileName;
        a.click();
        window.URL.revokeObjectURL(url);
      },
      error: () => {
        this.exporting = false;
      }
    });
  }

  private toDateInput(value: Date): string {
    const y = value.getFullYear();
    const m = String(value.getMonth() + 1).padStart(2, '0');
    const d = String(value.getDate()).padStart(2, '0');
    return `${y}-${m}-${d}`;
  }
}
