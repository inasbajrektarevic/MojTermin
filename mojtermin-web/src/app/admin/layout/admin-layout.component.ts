import { Component, OnDestroy, OnInit } from '@angular/core';
import { ActivatedRoute, NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { filter } from 'rxjs/operators';
import { Subscription, interval } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { ApiService } from '../../core/services/api.service';
import { ToastService } from '../../core/services/toast.service';

@Component({
  selector: 'app-admin-layout',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  templateUrl: './admin-layout.component.html',
  styleUrl: './admin-layout.component.scss'
})
export class AdminLayoutComponent implements OnInit, OnDestroy {
  readonly slug: string;
  navOpen = false;
  menuViewportPx: number | null = null;
  menuBottomPadPx: number | null = null;
  pendingAppointments = 0;
  private lastPendingAppointments: number | null = null;
  private pollingSub?: Subscription;
  private navCloseSub?: Subscription;
  private visibilityHandler?: () => void;
  private viewportSyncHandler?: () => void;

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

    this.navCloseSub = this.router.events.pipe(filter((e) => e instanceof NavigationEnd)).subscribe(() => {
      this.closeNav();
    });
  }

  toggleNav(): void {
    this.navOpen = !this.navOpen;
    if (this.navOpen) {
      queueMicrotask(() => this.syncMenuViewport());
      this.attachViewportSync();
    } else {
      this.detachViewportSync();
      this.menuViewportPx = null;
      this.menuBottomPadPx = null;
    }
  }

  closeNav(): void {
    this.navOpen = false;
    this.detachViewportSync();
    this.menuViewportPx = null;
    this.menuBottomPadPx = null;
  }

  ngOnDestroy(): void {
    this.stopPolling();
    this.navCloseSub?.unsubscribe();
    this.detachViewportSync();
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

  /** Real visible height + bottom inset for mobile browser chrome (Samsung/Brave). */
  private syncMenuViewport(): void {
    if (typeof window === 'undefined') {
      return;
    }
    const vv = window.visualViewport;
    const height = vv?.height ?? window.innerHeight;
    this.menuViewportPx = Math.round(height);

    const browserChrome = vv
      ? Math.max(0, window.innerHeight - vv.height - (vv.offsetTop ?? 0))
      : 0;
    // Extra tap room so last items clear bottom toolbar / floating widgets
    this.menuBottomPadPx = Math.round(Math.max(88, browserChrome + 40));
  }

  private attachViewportSync(): void {
    if (typeof window === 'undefined' || this.viewportSyncHandler) {
      return;
    }
    this.viewportSyncHandler = () => {
      if (this.navOpen) {
        this.syncMenuViewport();
      }
    };
    window.visualViewport?.addEventListener('resize', this.viewportSyncHandler);
    window.visualViewport?.addEventListener('scroll', this.viewportSyncHandler);
    window.addEventListener('resize', this.viewportSyncHandler);
    window.addEventListener('orientationchange', this.viewportSyncHandler);
  }

  private detachViewportSync(): void {
    if (typeof window === 'undefined' || !this.viewportSyncHandler) {
      return;
    }
    window.visualViewport?.removeEventListener('resize', this.viewportSyncHandler);
    window.visualViewport?.removeEventListener('scroll', this.viewportSyncHandler);
    window.removeEventListener('resize', this.viewportSyncHandler);
    window.removeEventListener('orientationchange', this.viewportSyncHandler);
    this.viewportSyncHandler = undefined;
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
