import { Component, OnInit, inject } from '@angular/core';
import { FormBuilder, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { ApiService } from '../../core/services/api.service';
import { StaffMember, StaffTimeOff } from '../../shared/models/business.models';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-staff',
  standalone: true,
  imports: [ReactiveFormsModule, FormsModule],
  templateUrl: './staff.component.html',
  styleUrl: './staff.component.scss'
})
export class StaffComponent implements OnInit {
  private readonly fb = inject(FormBuilder);
  private readonly apiService = inject(ApiService);
  private readonly toastService = inject(ToastService);

  rows: StaffMember[] = [];
  selectedStaff: StaffMember | null = null;
  timeOffs: StaffTimeOff[] = [];
  editingId: string | null = null;
  query = '';
  saving = false;
  loading = false;
  timeOffLoading = false;
  timeOffSaving = false;

  readonly form = this.fb.group({
    fullName: ['', [Validators.required, Validators.maxLength(150)]],
    title: ['', [Validators.maxLength(120)]],
    phone: ['', [Validators.maxLength(30)]],
    email: ['', [Validators.maxLength(120)]],
    isActive: [true]
  });

  /** Samo datumi — cjelodnevno odsustvo u rasponu (bez sati). */
  readonly timeOffForm = this.fb.group({
    dateFrom: ['', Validators.required],
    dateTo: ['', Validators.required],
    reason: ['', [Validators.maxLength(300)]]
  });

  ngOnInit(): void {
    this.load();
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const payload = this.form.getRawValue();
    this.saving = true;
    const request = this.editingId
      ? this.apiService.updateStaff(this.editingId, {
          fullName: (payload.fullName ?? '').trim(),
          title: (payload.title ?? '').trim() || undefined,
          phone: (payload.phone ?? '').trim() || undefined,
          email: (payload.email ?? '').trim() || undefined,
          isActive: !!payload.isActive
        })
      : this.apiService.createStaff({
          fullName: (payload.fullName ?? '').trim(),
          title: (payload.title ?? '').trim() || undefined,
          phone: (payload.phone ?? '').trim() || undefined,
          email: (payload.email ?? '').trim() || undefined
        });

    request.subscribe({
      next: () => {
        this.saving = false;
        this.cancelEdit();
        this.load();
        this.toastService.success('Zaposlenik je sačuvan.');
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  edit(row: StaffMember): void {
    this.editingId = row.id;
    this.selectedStaff = null;
    this.timeOffs = [];
    this.form.patchValue({
      fullName: row.fullName,
      title: row.title ?? '',
      phone: row.phone ?? '',
      email: row.email ?? '',
      isActive: row.isActive
    });
  }

  /** Otvara panel time-off; usklađuje formu ako je bio drugi zaposlenik u uređivanju. */
  openTimeOff(row: StaffMember): void {
    if (this.editingId !== row.id) {
      this.editingId = row.id;
      this.form.patchValue({
        fullName: row.fullName,
        title: row.title ?? '',
        phone: row.phone ?? '',
        email: row.email ?? '',
        isActive: row.isActive
      });
    }
    this.selectedStaff = row;
    this.loadTimeOffs(row.id);
  }

  closeTimeOff(): void {
    this.selectedStaff = null;
    this.timeOffs = [];
    this.timeOffForm.reset({
      dateFrom: '',
      dateTo: '',
      reason: ''
    });
  }

  cancelEdit(): void {
    this.editingId = null;
    this.selectedStaff = null;
    this.timeOffs = [];
    this.form.reset({
      fullName: '',
      title: '',
      phone: '',
      email: '',
      isActive: true
    });
    this.timeOffForm.reset({
      dateFrom: '',
      dateTo: '',
      reason: ''
    });
  }

  remove(id: string): void {
    if (!confirm('Obrisati zaposlenika?')) {
      return;
    }
    this.apiService.deleteStaff(id).subscribe({
      next: () => {
        if (this.editingId === id || this.selectedStaff?.id === id) {
          this.cancelEdit();
        }
        this.load();
        this.toastService.success('Zaposlenik je obrisan.');
      }
    });
  }

  get filteredRows(): StaffMember[] {
    const q = this.query.trim().toLowerCase();
    if (!q) {
      return this.rows;
    }
    return this.rows.filter(x =>
      x.fullName.toLowerCase().includes(q) ||
      (x.title ?? '').toLowerCase().includes(q) ||
      (x.email ?? '').toLowerCase().includes(q));
  }

  saveTimeOff(): void {
    if (!this.selectedStaff) {
      return;
    }
    if (this.timeOffForm.invalid) {
      this.timeOffForm.markAllAsTouched();
      return;
    }

    const payload = this.timeOffForm.getRawValue();
    this.timeOffSaving = true;
    this.apiService.createStaffTimeOff(this.selectedStaff.id, {
      dateFrom: payload.dateFrom ?? '',
      dateTo: payload.dateTo ?? '',
      reason: (payload.reason ?? '').trim() || undefined
    }).subscribe({
      next: () => {
        this.timeOffSaving = false;
        this.toastService.success('Time-off je sačuvan.');
        this.timeOffForm.patchValue({ reason: '' });
        this.loadTimeOffs(this.selectedStaff!.id);
      },
      error: () => {
        this.timeOffSaving = false;
      }
    });
  }

  removeTimeOff(id: string): void {
    if (!this.selectedStaff) {
      return;
    }
    if (!confirm('Obrisati ovaj time-off?')) {
      return;
    }
    this.apiService.deleteStaffTimeOff(this.selectedStaff.id, id).subscribe({
      next: () => {
        this.toastService.success('Time-off je obrisan.');
        this.loadTimeOffs(this.selectedStaff!.id);
      }
    });
  }

  formatDate(value: string): string {
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) {
      return value;
    }
    const dd = String(d.getDate()).padStart(2, '0');
    const mm = String(d.getMonth() + 1).padStart(2, '0');
    const yyyy = d.getFullYear();
    return `${dd}.${mm}.${yyyy}`;
  }

  /** Stari zapisi mogu imati satnice; novi su uvijek cjelodnevni. */
  formatTimeOffWindow(row: StaffTimeOff): string {
    if (!row.timeFrom || !row.timeTo) {
      return 'Cijeli dan';
    }
    return `${this.formatTimePart(row.timeFrom)} – ${this.formatTimePart(row.timeTo)}`;
  }

  private formatTimePart(value: string): string {
    const parts = value.split(':');
    if (parts.length >= 2) {
      return `${parts[0].padStart(2, '0')}:${parts[1].padStart(2, '0')}`;
    }
    return value;
  }

  private load(): void {
    this.loading = true;
    this.apiService.getStaff().subscribe({
      next: (rows) => {
        this.rows = rows;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
      }
    });
  }

  private loadTimeOffs(staffId: string): void {
    this.timeOffLoading = true;
    this.apiService.getStaffTimeOffs(staffId).subscribe({
      next: (rows) => {
        this.timeOffs = rows;
        this.timeOffLoading = false;
      },
      error: () => {
        this.timeOffs = [];
        this.timeOffLoading = false;
      }
    });
  }
}
