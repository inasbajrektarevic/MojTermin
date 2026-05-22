import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  AdminAuditLog,
  Appointment,
  Business,
  Client,
  DashboardSummary,
  NotificationLog,
  NotificationStatus,
  PublicAppointmentAvailability,
  PublicAppointmentSummary,
  Service,
  StaffMember,
  StaffTimeOff,
  WorkingHour
} from '../../shared/models/business.models';

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly api = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  getBusinessBySlug(slug: string): Observable<Business> {
    return this.http.get<Business>(`${this.api}/businesses/by-slug/${slug}`);
  }

  uploadLogo(file: File): Observable<{ url: string; fileName: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ url: string; fileName: string }>(`${this.api}/uploads/logo`, formData);
  }

  /** Admin: service card image; requires Owner JWT (same as other admin APIs). */
  uploadServiceImage(file: File): Observable<{ url: string; fileName: string }> {
    const formData = new FormData();
    formData.append('file', file);
    return this.http.post<{ url: string; fileName: string }>(`${this.api}/uploads/service-image`, formData);
  }

  getCurrentBusiness(): Observable<Business> {
    return this.http.get<Business>(`${this.api}/businesses/current`);
  }

  updateBusiness(payload: Partial<Business>): Observable<Business> {
    return this.http.put<Business>(`${this.api}/businesses/current`, payload);
  }

  getPublicServices(slug: string): Observable<Service[]> {
    return this.http.get<Service[]>(`${this.api}/services/public/${slug}`);
  }

  getPublicStaff(slug: string): Observable<StaffMember[]> {
    return this.http.get<StaffMember[]>(`${this.api}/staff/public/${slug}`);
  }

  getServices(): Observable<Service[]> {
    return this.http.get<Service[]>(`${this.api}/services`);
  }

  getStaff(): Observable<StaffMember[]> {
    return this.http.get<StaffMember[]>(`${this.api}/staff`);
  }

  createStaff(payload: {
    fullName: string;
    title?: string;
    phone?: string;
    email?: string;
  }): Observable<StaffMember> {
    return this.http.post<StaffMember>(`${this.api}/staff`, payload);
  }

  updateStaff(id: string, payload: {
    fullName: string;
    title?: string;
    phone?: string;
    email?: string;
    isActive: boolean;
  }): Observable<StaffMember> {
    return this.http.put<StaffMember>(`${this.api}/staff/${id}`, payload);
  }

  deleteStaff(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/staff/${id}`);
  }

  getStaffTimeOffs(staffId: string): Observable<StaffTimeOff[]> {
    return this.http.get<StaffTimeOff[]>(`${this.api}/staff/${staffId}/time-offs`);
  }

  createStaffTimeOff(staffId: string, payload: {
    dateFrom: string;
    dateTo: string;
    reason?: string;
  }): Observable<StaffTimeOff> {
    return this.http.post<StaffTimeOff>(`${this.api}/staff/${staffId}/time-offs`, payload);
  }

  deleteStaffTimeOff(staffId: string, timeOffId: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/staff/${staffId}/time-offs/${timeOffId}`);
  }

  createService(payload: Partial<Service>): Observable<Service> {
    return this.http.post<Service>(`${this.api}/services`, payload);
  }

  updateService(id: string, payload: Partial<Service>): Observable<Service> {
    return this.http.put<Service>(`${this.api}/services/${id}`, payload);
  }

  deleteService(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/services/${id}`);
  }

  getPublicWorkingHours(slug: string): Observable<WorkingHour[]> {
    return this.http.get<WorkingHour[]>(`${this.api}/working-hours/public/${slug}`);
  }

  getWorkingHours(): Observable<WorkingHour[]> {
    return this.http.get<WorkingHour[]>(`${this.api}/working-hours`);
  }

  updateWorkingHours(payload: Partial<WorkingHour>[]): Observable<WorkingHour[]> {
    return this.http.put<WorkingHour[]>(`${this.api}/working-hours`, payload);
  }

  getClients(): Observable<Client[]> {
    return this.http.get<Client[]>(`${this.api}/clients`);
  }

  createClient(payload: {
    fullName: string;
    phone: string;
    email?: string;
    note?: string;
  }): Observable<Client> {
    return this.http.post<Client>(`${this.api}/clients`, payload);
  }

  updateClient(id: string, payload: {
    fullName: string;
    phone: string;
    email?: string;
    note?: string;
  }): Observable<Client> {
    return this.http.put<Client>(`${this.api}/clients/${id}`, payload);
  }

  deleteClient(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/clients/${id}`);
  }

  exportClientsCsv(): Observable<Blob> {
    return this.http.get(`${this.api}/clients/export.csv`, { responseType: 'blob' });
  }

  getAppointments(): Observable<Appointment[]> {
    return this.http.get<Appointment[]>(`${this.api}/appointments`);
  }

  getPublicAvailability(slug: string, serviceId: string, date: string, staffMemberId?: string): Observable<PublicAppointmentAvailability> {
    const params: Record<string, string> = { serviceId, date };
    if (staffMemberId) {
      params['staffMemberId'] = staffMemberId;
    }
    return this.http.get<PublicAppointmentAvailability>(
      `${this.api}/appointments/public/${slug}/availability`,
      { params }
    );
  }

  lookupCancelAppointment(token: string): Observable<PublicAppointmentSummary> {
    return this.http.get<PublicAppointmentSummary>(
      `${this.api}/appointments/public/cancel/lookup`,
      {
        params: { token }
      }
    );
  }

  cancelAppointmentByToken(token: string): Observable<PublicAppointmentSummary> {
    return this.http.post<PublicAppointmentSummary>(
      `${this.api}/appointments/public/cancel`,
      { token }
    );
  }

  getDashboardSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${this.api}/dashboard/summary`);
  }

  exportRevenueCsv(from?: string, to?: string): Observable<Blob> {
    const params: Record<string, string> = {};
    if (from) {
      params['from'] = from;
    }
    if (to) {
      params['to'] = to;
    }
    return this.http.get(`${this.api}/dashboard/revenue/export.csv`, {
      responseType: 'blob',
      params
    });
  }

  getNotifications(filters?: {
    status?: NotificationStatus;
    from?: string;
    to?: string;
    limit?: number;
  }): Observable<NotificationLog[]> {
    const params: Record<string, string> = {};
    if (filters?.status) {
      params['status'] = String(filters.status);
    }
    if (filters?.from) {
      params['from'] = filters.from;
    }
    if (filters?.to) {
      params['to'] = filters.to;
    }
    if (typeof filters?.limit === 'number') {
      params['limit'] = String(filters.limit);
    }

    return this.http.get<NotificationLog[]>(`${this.api}/notifications`, { params });
  }

  getAdminAuditLogs(filters?: {
    resourceType?: string;
    action?: string;
    from?: string;
    to?: string;
    limit?: number;
  }): Observable<AdminAuditLog[]> {
    const params: Record<string, string> = {};
    if (filters?.resourceType) {
      params['resourceType'] = filters.resourceType;
    }
    if (filters?.action) {
      params['action'] = filters.action;
    }
    if (filters?.from) {
      params['from'] = filters.from;
    }
    if (filters?.to) {
      params['to'] = filters.to;
    }
    if (typeof filters?.limit === 'number') {
      params['limit'] = String(filters.limit);
    }
    return this.http.get<AdminAuditLog[]>(`${this.api}/admin-audit`, { params });
  }

  bookAppointmentPublic(slug: string, payload: {
    serviceId: string;
    staffMemberId?: string;
    appointmentDate: string;
    startTime: string;
    fullName: string;
    phone: string;
    email?: string;
    note?: string;
    // Honeypot field. The frontend form keeps this hidden via CSS so real users
    // never see it; bots auto-filling all inputs trigger a server-side reject.
    // Always sent as empty string from legit clients.
    website?: string;
  }): Observable<Appointment> {
    return this.http.post<Appointment>(`${this.api}/appointments/public/${slug}`, payload);
  }

  updateAppointmentStatus(id: string, action: 'confirm' | 'cancel' | 'complete'): Observable<void> {
    return this.http.put<void>(`${this.api}/appointments/${id}/${action}`, {});
  }

  deleteAppointment(id: string): Observable<void> {
    return this.http.delete<void>(`${this.api}/appointments/${id}`);
  }
}
