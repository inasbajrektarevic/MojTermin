import { Routes } from '@angular/router';
import { authGuard } from './core/auth/auth.guard';
import { publicBusinessResolver } from './core/resolvers/public-business.resolver';
import { servicesResolver } from './core/resolvers/services.resolver';
import { appointmentsResolver } from './core/resolvers/appointments.resolver';
import { dashboardResolver } from './core/resolvers/dashboard.resolver';
import { clientsResolver } from './core/resolvers/clients.resolver';
import { workingHoursResolver } from './core/resolvers/working-hours.resolver';
import { businessProfileResolver } from './core/resolvers/business-profile.resolver';

export const routes: Routes = [
  {
    path: '',
    loadComponent: () =>
      import('./public/platform-home/platform-home.component').then((m) => m.PlatformHomeComponent)
  },
  {
    path: 'register-business',
    loadComponent: () =>
      import('./public/register-business/register-business.component').then((m) => m.RegisterBusinessComponent)
  },
  {
    path: 'verify-email',
    loadComponent: () =>
      import('./public/verify-email/verify-email.component').then((m) => m.VerifyEmailComponent)
  },
  {
    path: 'forgot-password',
    loadComponent: () =>
      import('./public/forgot-password/forgot-password.component').then((m) => m.ForgotPasswordComponent)
  },
  {
    path: 'reset-password',
    loadComponent: () =>
      import('./public/reset-password/reset-password.component').then((m) => m.ResetPasswordComponent)
  },
  {
    path: 'cancel-appointment',
    loadComponent: () =>
      import('./public/cancel-appointment/cancel-appointment.component').then((m) => m.CancelAppointmentComponent)
  },
  {
    path: 'b/:slug/admin/login',
    loadComponent: () => import('./admin/login/login.component').then((m) => m.LoginComponent)
  },
  {
    path: 'b/:slug/admin',
    canActivate: [authGuard],
    loadComponent: () =>
      import('./admin/layout/admin-layout.component').then((m) => m.AdminLayoutComponent),
    children: [
      { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
      {
        path: 'dashboard',
        resolve: { summary: dashboardResolver },
        loadComponent: () =>
          import('./admin/dashboard/dashboard.component').then((m) => m.DashboardComponent)
      },
      {
        path: 'appointments',
        resolve: { appointments: appointmentsResolver },
        loadComponent: () =>
          import('./admin/appointments/appointments.component').then((m) => m.AppointmentsComponent)
      },
      {
        path: 'services',
        resolve: { services: servicesResolver },
        loadComponent: () => import('./admin/services/services.component').then((m) => m.ServicesComponent)
      },
      {
        path: 'staff',
        loadComponent: () => import('./admin/staff/staff.component').then((m) => m.StaffComponent)
      },
      {
        path: 'clients',
        resolve: { clients: clientsResolver },
        loadComponent: () => import('./admin/clients/clients.component').then((m) => m.ClientsComponent)
      },
      {
        path: 'working-hours',
        resolve: { workingHours: workingHoursResolver },
        loadComponent: () =>
          import('./admin/working-hours/working-hours.component').then((m) => m.WorkingHoursComponent)
      },
      {
        path: 'business-profile',
        resolve: { business: businessProfileResolver },
        loadComponent: () =>
          import('./admin/business-profile/business-profile.component').then((m) => m.BusinessProfileComponent)
      },
      {
        path: 'notifications',
        loadComponent: () =>
          import('./admin/notifications/notifications.component').then((m) => m.NotificationsComponent)
      },
      {
        path: 'audit',
        loadComponent: () =>
          import('./admin/audit/audit.component').then((m) => m.AuditComponent)
      }
    ]
  },
  {
    path: 'b/:slug/book',
    resolve: { preload: publicBusinessResolver },
    loadComponent: () =>
      import('./public/booking-page/booking-page.component').then((m) => m.BookingPageComponent)
  },
  {
    path: 'b/:slug',
    resolve: { preload: publicBusinessResolver },
    loadComponent: () =>
      import('./public/business-page/business-page.component').then((m) => m.BusinessPageComponent)
  },
  // Legacy /admin paths without a tenant slug bounce to the platform home.
  // Users must pick their tenant explicitly (their bookmark/email link contains the slug).
  { path: 'admin/login', redirectTo: '', pathMatch: 'full' },
  { path: 'admin', redirectTo: '', pathMatch: 'full' },
  {
    path: '**',
    loadComponent: () =>
      import('./public/not-found/not-found.component').then((m) => m.NotFoundComponent)
  }
];
