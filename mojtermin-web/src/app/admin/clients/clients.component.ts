import { Component, OnInit, inject } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators, FormsModule } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { Client } from '../../shared/models/business.models';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-clients',
  standalone: true,
  imports: [DatePipe, ReactiveFormsModule, FormsModule],
  templateUrl: './clients.component.html',
  styleUrl: './clients.component.scss'
})
export class ClientsComponent implements OnInit {
  private readonly emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
  private readonly phoneRegex = /^\+(387|43|386|49|45|41|385|382)[0-9\s\-\/()]{6,20}$/;

  private readonly fb = inject(FormBuilder);

  clients: Client[] = [];
  editingId: string | null = null;
  saving = false;
  query = '';
  loading = false;
  loadError = '';
  exporting = false;

  readonly form = this.fb.group({
    fullName: ['', [Validators.required, Validators.maxLength(150)]],
    phone: ['', [Validators.required, Validators.maxLength(30), Validators.pattern(this.phoneRegex)]],
    email: [''],
    note: ['']
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

  ngOnInit(): void {
    this.load();
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload = this.form.getRawValue();
    const email = (payload.email ?? '').trim();
    const note = (payload.note ?? '').trim();

    if (email && !this.emailRegex.test(email)) {
      this.toastService.error('Unesite ispravnu email adresu ili ostavite polje prazno.');
      return;
    }

    const phone = (payload.phone ?? '').trim();
    if (!this.phoneRegex.test(phone)) {
      this.toastService.error('Telefon mora početi sa +387, +43, +386, +49, +45, +41, +385 ili +382.');
      return;
    }

    const request = this.editingId
      ? this.apiService.updateClient(this.editingId, {
          fullName: (payload.fullName ?? '').trim(),
          phone,
          email: email || undefined,
          note: note || undefined
        })
      : this.apiService.createClient({
          fullName: (payload.fullName ?? '').trim(),
          phone,
          email: email || undefined,
          note: note || undefined
        });

    this.saving = true;
    request.subscribe({
      next: () => {
        this.saving = false;
        this.editingId = null;
        this.form.reset({ fullName: '', phone: '', email: '', note: '' });
        this.load();
        this.toastService.success('Klijent je sačuvan.');
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  edit(client: Client): void {
    this.editingId = client.id;
    this.form.patchValue({
      fullName: client.fullName,
      phone: client.phone,
      email: client.email ?? '',
      note: client.note ?? ''
    });
  }

  cancelEdit(): void {
    this.editingId = null;
    this.form.reset({ fullName: '', phone: '', email: '', note: '' });
  }

  remove(clientId: string): void {
    if (!confirm('Da li ste sigurni da želite obrisati klijenta?')) {
      return;
    }

    this.apiService.deleteClient(clientId).subscribe(() => {
      this.load();
      this.toastService.success('Klijent je obrisan.');
    });
  }

  exportCsv(): void {
    if (this.exporting) {
      return;
    }

    this.exporting = true;
    this.apiService.exportClientsCsv().subscribe({
      next: (blob) => {
        this.exporting = false;
        const now = new Date();
        const stamp = `${now.getFullYear()}${String(now.getMonth() + 1).padStart(2, '0')}${String(now.getDate()).padStart(2, '0')}-${String(now.getHours()).padStart(2, '0')}${String(now.getMinutes()).padStart(2, '0')}`;
        const fileName = `klijenti-${stamp}.csv`;
        const url = window.URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        anchor.click();
        window.URL.revokeObjectURL(url);
        this.toastService.success('CSV fajl je preuzet.');
      },
      error: () => {
        this.exporting = false;
        this.toastService.error('Izvoz klijenata nije uspio.');
      }
    });
  }

  private load(): void {
    const resolved = (this.route.snapshot.data['clients'] as Client[] | undefined) ?? [];
    if (resolved.length && this.clients.length === 0) {
      this.clients = resolved;
      return;
    }
    this.loading = true;
    this.loadError = '';
    this.apiService.getClients().subscribe({
      next: (rows) => {
        this.clients = rows;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.loadError = 'Neuspješno učitavanje klijenata.';
      }
    });
  }

  get filteredClients(): Client[] {
    const query = this.query.trim().toLowerCase();
    if (!query) {
      return this.clients;
    }

    return this.clients.filter((x) =>
      x.fullName.toLowerCase().includes(query) ||
      x.phone.toLowerCase().includes(query) ||
      (x.email ?? '').toLowerCase().includes(query)
    );
  }

  fieldError(fieldName: 'fullName' | 'phone'): string {
    const control = this.form.get(fieldName);
    if (!control || !(control.touched || control.dirty) || !control.errors) {
      return '';
    }

    if (control.errors['required']) {
      return 'Polje je obavezno.';
    }

    if (control.errors['maxlength']) {
      return fieldName === 'fullName'
        ? 'Maksimalna dužina je 150 karaktera.'
        : 'Maksimalna dužina je 30 karaktera.';
    }

    if (fieldName === 'phone' && control.errors['pattern']) {
      return 'Dozvoljeni pozivni brojevi: +387, +43, +386, +49, +45, +41, +385, +382.';
    }

    return 'Neispravan unos.';
  }
}
