import { Component, OnInit, inject } from '@angular/core';
import { FormArray, FormBuilder, FormGroup, ReactiveFormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { WorkingHour } from '../../shared/models/business.models';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-working-hours',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './working-hours.component.html',
  styleUrl: './working-hours.component.scss'
})
export class WorkingHoursComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly sundayDefaultOpenTime = '07:00:00';
  private readonly sundayDefaultCloseTime = '12:00:00';

  readonly days = ['Nedjelja', 'Ponedjeljak', 'Utorak', 'Srijeda', 'Četvrtak', 'Petak', 'Subota'];
  readonly rows = new FormArray<FormGroup>([]);
  saving = false;
  loading = false;
  loadError = '';

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

  ngOnInit(): void {
    const resolved = (this.route.snapshot.data['workingHours'] as WorkingHour[] | undefined) ?? [];
    if (resolved.length) {
      this.bindRows(resolved);
      return;
    }

    this.loading = true;
    this.apiService.getWorkingHours().subscribe({
      next: (hours) => {
        this.bindRows(hours);
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.loadError = 'Neuspješno učitavanje radnog vremena.';
      }
    });
  }

  get formRows(): FormArray<FormGroup> {
    return this.rows;
  }

  save(): void {
    const hasInvalidTime = this.rows.controls.some((row) =>
      !this.isValidTimeInput(String(row.get('openTime')?.value ?? '')) ||
      !this.isValidTimeInput(String(row.get('closeTime')?.value ?? ''))
    );
    if (hasInvalidTime) {
      this.toastService.error('Vrijeme unesite u 24h formatu, npr. 07:00 ili 19:00.');
      return;
    }

    const payload = (this.rows.getRawValue() as Partial<WorkingHour>[]).map((row) => ({
      ...row,
      openTime: this.toTimeSpanString(row.openTime),
      closeTime: this.toTimeSpanString(row.closeTime)
    }));
    this.saving = true;
    this.apiService.updateWorkingHours(payload).subscribe({
      next: () => {
        this.saving = false;
        this.toastService.success('Radni sati su sačuvani.');
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  onClosedToggle(index: number): void {
    if (index !== 0) {
      return;
    }

    const row = this.rows.at(index);
    const isClosed = !!row.get('isClosed')?.value;
    if (!isClosed) {
      const confirmed = confirm('Da li zaista želite otvoriti nedjeljom?');
      if (!confirmed) {
        row.patchValue({ isClosed: true }, { emitEvent: false });
      }
    }
  }

  private toTimeSpanString(value: unknown): string {
    if (typeof value !== 'string') {
      return '00:00:00';
    }

    const trimmed = value.trim();
    if (!trimmed) {
      return '00:00:00';
    }

    // Already in 24h format.
    if (/^\d{2}:\d{2}(:\d{2})?$/.test(trimmed)) {
      return trimmed.length === 5 ? `${trimmed}:00` : trimmed;
    }

    // Convert 12h values from browser locale, e.g. "07:00 AM".
    const twelveHourMatch = trimmed.match(/^(\d{1,2}):(\d{2})\s*([AP]M)$/i);
    if (twelveHourMatch) {
      let hour = Number(twelveHourMatch[1]);
      const minute = Number(twelveHourMatch[2]);
      const marker = twelveHourMatch[3].toUpperCase();

      if (marker === 'AM' && hour === 12) {
        hour = 0;
      } else if (marker === 'PM' && hour < 12) {
        hour += 12;
      }

      return `${hour.toString().padStart(2, '0')}:${minute.toString().padStart(2, '0')}:00`;
    }

    return '00:00:00';
  }

  private toDisplayTime(value: string): string {
    return this.toTimeSpanString(value).slice(0, 5);
  }

  private isValidTimeInput(value: string): boolean {
    const trimmed = value.trim();
    const match = trimmed.match(/^(\d{1,2}):(\d{2})$/);
    if (!match) {
      return false;
    }

    const hour = Number(match[1]);
    const minute = Number(match[2]);
    return hour >= 0 && hour <= 23 && minute >= 0 && minute <= 59;
  }

  private bindRows(hours: WorkingHour[]): void {
    this.loadError = '';
    this.rows.clear();
    for (const day of this.days.keys()) {
      const existing = hours.find((x) => x.dayOfWeek === day);
      const isSunday = day === 0;
      const sundayFromMidnight =
        existing?.openTime?.startsWith('00:00') && existing?.closeTime?.startsWith('00:00');
      this.rows.push(
        this.fb.group({
          dayOfWeek: [day],
          openTime: [
            this.toDisplayTime(isSunday && (!existing || sundayFromMidnight)
              ? this.sundayDefaultOpenTime
              : existing?.openTime ?? '08:00:00')
          ],
          closeTime: [
            this.toDisplayTime(isSunday && (!existing || sundayFromMidnight)
              ? this.sundayDefaultCloseTime
              : existing?.closeTime ?? '16:00:00')
          ],
          isClosed: [existing?.isClosed ?? isSunday]
        })
      );
    }
  }
}
