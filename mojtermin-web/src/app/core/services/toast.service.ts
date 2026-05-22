import { Injectable, signal } from '@angular/core';

export type ToastType = 'success' | 'error' | 'info';

export interface ToastMessage {
  id: number;
  message: string;
  type: ToastType;
}

@Injectable({ providedIn: 'root' })
export class ToastService {
  private counter = 0;
  readonly toasts = signal<ToastMessage[]>([]);

  success(message: string): void {
    this.push(message, 'success');
  }

  error(message: string): void {
    this.push(message, 'error');
  }

  info(message: string): void {
    this.push(message, 'info');
  }

  remove(id: number): void {
    this.toasts.update((items) => items.filter((x) => x.id !== id));
  }

  private push(message: string, type: ToastType): void {
    const id = ++this.counter;
    this.toasts.update((items) => [...items, { id, message, type }]);
    setTimeout(() => this.remove(id), 3500);
  }
}
