import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, Router, RouterLink, RouterOutlet } from '@angular/router';
import { Subscription, interval } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [RouterLink, RouterOutlet],
  templateUrl: './admin-layout.component.html',
  styleUrl: './admin-layout.component.scss'
})
export class AdminLayoutComponent implements OnInit, OnDestroy {
  readonly slug: string;
  pendingAppointments = 0;
  private lastPendingAppointments: number | null = null;
  private pollingSub?: Subscription;
  private visibilityHandler?: () => void;

  constructor(
    private readonly authService: AuthService,
    private readonly apiService: ApiService,
    private readonly toastService: ToastService,
    private readonly router: Router,
    private readonly route: ActivatedRoute
  ) {
    // Slug is provided by the parent route ('b/:slug/admin') so it is always
    // present here; fall back to empty so logout() can still navigate sanely.
    this.slug = this.route.snapshot.paramMap.get('slug') ?? '';
  }

  ngOnInit(): void {
    if (typeof window !== 'undefined' && 'Notification' in window && Notification.permission === 'default') {
      Notification.requestPermission().catch(() => {});
    }

    this.startPolling();

    if (typeof document !== 'undefined') {
      this.visibilityHandler = () => {
        if (document.hidden) {
          this.stopPolling();
        } else {
          this.startPolling();
          this.refreshPendingCount();
        }
      };
      document.addEventListener('visibilitychange', this.visibilityHandler);
    }

    this.refreshPendingCount();
  }

  ngOnDestroy(): void {
    this.stopPolling();
    if (this.visibilityHandler && typeof document !== 'undefined') {
      document.removeEventListener('visibilitychange', this.visibilityHandler);
    }
  }

  logout(): void {
    this.stopPolling();
    this.authService.logout();
    if (this.slug) {
      this.router.navigate(['/b', this.slug, 'admin', 'login']);
    } else {
      this.router.navigate(['/']);
    }
  }

  private startPolling(): void {
    if (this.pollingSub && !this.pollingSub.closed) {
      return;
    }
    this.pollingSub = interval(10000).subscribe(() => this.refreshPendingCount());
  }

  private stopPolling(): void {
    this.pollingSub?.unsubscribe();
    this.pollingSub = undefined;
  }

  private refreshPendingCount(): void {
    this.apiService.getDashboardSummary().subscribe({
      next: (summary) => {
        const nextPending = summary.pendingAppointments;
        const hadPrevious = this.lastPendingAppointments !== null;
        const increased = hadPrevious && nextPending > (this.lastPendingAppointments ?? 0);
        this.pendingAppointments = nextPending;
        this.lastPendingAppointments = nextPending;

        if (increased) {
          this.toastService.info('Stigao je novi zahtjev za termin.');
          this.notifyBrowser(nextPending);
        }
      },
      error: () => {
        // Silent fail to avoid noisy nav errors during transient network issues.
      }
    });

  }

  private notifyBrowser(pendingAppointments: number): void {
    if (typeof window === 'undefined' || !('Notification' in window)) {
      return;
    }

    if (Notification.permission === 'granted') {
      new Notification('MojTermin', {
        body: `Imate ${pendingAppointments} termina na čekanju.`
      });
    }
  }

}
