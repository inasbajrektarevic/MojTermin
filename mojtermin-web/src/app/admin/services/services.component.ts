import { Component, ElementRef, OnInit, ViewChild, inject } from '@angular/core';
import { CurrencyPipe } from '@angular/common';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute } from '@angular/router';
import { ApiService } from '../../core/services/api.service';
import { Service } from '../../shared/models/business.models';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-services',
  standalone: true,
  imports: [ReactiveFormsModule, CurrencyPipe],
  templateUrl: './services.component.html',
  styleUrl: './services.component.scss'
})
export class ServicesComponent implements OnInit {
  private readonly fb = inject(FormBuilder);

  @ViewChild('serviceImageFile') private serviceImageFile?: ElementRef<HTMLInputElement>;

  services: Service[] = [];
  editingId: string | null = null;
  saving = false;
  loading = false;
  loadError = '';
  uploadingServiceImage = false;

  private readonly maxServiceImageBytes = 5 * 1024 * 1024;

  readonly form = this.fb.group({
    name: ['', [Validators.required, Validators.maxLength(150)]],
    description: [''],
    imageUrl: ['', Validators.maxLength(500)],
    durationMinutes: [30, [Validators.required, Validators.min(1)]],
    price: [0, [Validators.required, Validators.min(0)]]
  });

  constructor(
    private readonly route: ActivatedRoute,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService
  ) {}

  ngOnInit(): void {
    this.services = (this.route.snapshot.data['services'] as Service[] | undefined) ?? [];
    if (!this.services.length) {
      this.load();
    }
  }

  save(): void {
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }

    const payload = this.form.getRawValue();
    const requestPayload = {
      name: (payload.name ?? '').trim(),
      description: (payload.description ?? '').trim() || undefined,
      imageUrl: (payload.imageUrl ?? '').trim() || undefined,
      durationMinutes: payload.durationMinutes ?? 30,
      price: payload.price ?? 0
    };
    const request = this.editingId
      ? this.apiService.updateService(this.editingId, requestPayload)
      : this.apiService.createService(requestPayload);

    this.saving = true;
    request.subscribe({
      next: () => {
        this.form.reset({ name: '', description: '', imageUrl: '', durationMinutes: 30, price: 0 });
        this.editingId = null;
        this.resetServiceImageFileInput();
        this.load();
        this.saving = false;
        this.toastService.success('Usluga je sačuvana.');
      },
      error: () => {
        this.saving = false;
      }
    });
  }

  edit(service: Service): void {
    this.editingId = service.id;
    this.resetServiceImageFileInput();
    this.form.patchValue({
      name: service.name,
      description: service.description ?? '',
      imageUrl: service.imageUrl ?? '',
      durationMinutes: service.durationMinutes,
      price: service.price
    });
  }

  cancelEdit(): void {
    this.editingId = null;
    this.resetServiceImageFileInput();
    this.form.reset({ name: '', description: '', imageUrl: '', durationMinutes: 30, price: 0 });
  }

  remove(id: string): void {
    if (!confirm('Da li ste sigurni da želite obrisati uslugu?')) {
      return;
    }
    this.apiService.deleteService(id).subscribe(() => {
      this.load();
      this.toastService.success('Usluga je obrisana.');
    });
  }

  private load(): void {
    this.loading = true;
    this.loadError = '';
    this.apiService.getServices().subscribe({
      next: (rows) => {
        this.services = rows;
        this.loading = false;
      },
      error: () => {
        this.loading = false;
        this.loadError = 'Neuspješno učitavanje usluga.';
      }
    });
  }

  fieldError(fieldName: 'name' | 'durationMinutes' | 'price'): string {
    const control = this.form.get(fieldName);
    if (!control || !(control.touched || control.dirty) || !control.errors) {
      return '';
    }

    if (control.errors['required']) {
      return 'Polje je obavezno.';
    }

    if (control.errors['maxlength']) {
      return 'Maksimalna dužina je 150 karaktera.';
    }

    if (control.errors['min']) {
      return fieldName === 'durationMinutes'
        ? 'Trajanje mora biti najmanje 1 minuta.'
        : 'Cijena ne može biti negativna.';
    }

    return 'Neispravan unos.';
  }

  onServiceImageSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) {
      return;
    }

    const err = this.validateServiceImageFile(file);
    if (err) {
      this.toastService.error(err);
      input.value = '';
      return;
    }

    this.uploadingServiceImage = true;
    this.apiService.uploadServiceImage(file).subscribe({
      next: (res) => {
        this.uploadingServiceImage = false;
        input.value = '';
        this.form.patchValue({ imageUrl: res.url });
        this.toastService.success('Slika je uploadovana.');
      },
      error: () => {
        this.uploadingServiceImage = false;
        input.value = '';
        this.toastService.error('Upload slike nije uspio.');
      }
    });
  }

  clearServiceImage(): void {
    this.form.patchValue({ imageUrl: '' });
    this.resetServiceImageFileInput();
  }

  private resetServiceImageFileInput(): void {
    const el = this.serviceImageFile?.nativeElement;
    if (el) {
      el.value = '';
    }
  }

  private validateServiceImageFile(file: File): string | null {
    const okTypes = ['image/jpeg', 'image/png', 'image/webp'];
    if (!okTypes.includes(file.type)) {
      return 'Dozvoljeni su JPG, PNG i WEBP.';
    }
    if (file.size <= 0) {
      return 'Datoteka je prazna.';
    }
    if (file.size > this.maxServiceImageBytes) {
      return 'Slika je prevelika (maks. 5 MB).';
    }
    return null;
  }
}
