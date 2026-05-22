export type AppointmentStatus = 'Pending' | 'Confirmed' | 'Rejected' | 'Cancelled' | 'Completed' | 1 | 2 | 3 | 4 | 5;

export interface Business {
  id: string;
  name: string;
  slug: string;
  businessType: number;
  phone: string;
  email: string;
  address: string;
  description: string;
  logoUrl?: string | null;
  coverImageUrl?: string | null;
  themePreset: string;
  primaryColor?: string | null;
  secondaryColor?: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface Service {
  id: string;
  businessId: string;
  name: string;
  description?: string | null;
  imageUrl?: string | null;
  durationMinutes: number;
  price: number;
  isActive: boolean;
  createdAt: string;
}

export interface WorkingHour {
  id: string;
  businessId: string;
  dayOfWeek: number;
  openTime: string;
  closeTime: string;
  isClosed: boolean;
}

export interface Client {
  id: string;
  businessId: string;
  fullName: string;
  phone: string;
  email?: string | null;
  note?: string | null;
  createdAt: string;
}

export interface Appointment {
  id: string;
  businessId: string;
  serviceId: string;
  clientId: string;
  staffMemberId?: string | null;
  appointmentDate: string;
  startTime: string;
  endTime: string;
  status: AppointmentStatus;
  note?: string | null;
  createdAt: string;
  serviceName: string;
  clientName: string;
  staffMemberName: string;
}

export interface StaffMember {
  id: string;
  businessId: string;
  fullName: string;
  title?: string | null;
  phone?: string | null;
  email?: string | null;
  isActive: boolean;
  createdAt: string;
}

export interface StaffTimeOff {
  id: string;
  businessId: string;
  staffMemberId: string;
  dateFrom: string;
  dateTo: string;
  timeFrom?: string | null;
  timeTo?: string | null;
  reason?: string | null;
  createdAtUtc: string;
}

export interface PublicAppointmentAvailability {
  appointmentDate: string;
  serviceId: string;
  availableStartTimes: string[];
  slots: PublicAppointmentSlot[];
}

export interface PublicAppointmentSlot {
  startTime: string;
  isAvailable: boolean;
  unavailableReason?: 'Past' | 'Booked' | null;
}

export interface PublicAppointmentSummary {
  businessName: string;
  serviceName: string;
  clientFirstName: string;
  appointmentDate: string;
  startTime: string;
  endTime: string;
  alreadyCancelled: boolean;
  tooLateToCancel: boolean;
}

export interface DashboardSummary {
  todaysAppointments: number;
  thisWeekAppointments: number;
  newClients: number;
  pendingAppointments: number;
}

export type NotificationStatus = 'Skipped' | 'Sent' | 'Failed' | 1 | 2 | 3;

export interface NotificationLog {
  id: string;
  businessId: string;
  appointmentId?: string | null;
  channel: 'Email';
  status: NotificationStatus;
  recipient: string;
  subject: string;
  bodyPreview: string;
  errorMessage?: string | null;
  createdAtUtc: string;
  sentAtUtc?: string | null;
}

export interface AdminAuditLog {
  id: string;
  businessId: string;
  actorUserId?: string | null;
  actorName: string;
  actorEmail: string;
  action: string;
  resourceType: string;
  resourceId?: string | null;
  summary?: string | null;
  metadataJson?: string | null;
  createdAtUtc: string;
}

export interface AuthResponse {
  token: string;
  refreshToken: string;
  expiresAtUtc: string;
  userId: string;
  businessId: string;
  fullName: string;
  username: string;
  role: string;
}

export interface RegisterBusinessRequest {
  businessName: string;
  slug: string;
  businessType: number;
  phone: string;
  businessEmail: string;
  address: string;
  description: string;
  logoUrl?: string;
  ownerFullName: string;
  ownerEmail: string;
  ownerUsername: string;
  ownerPassword: string;
}

/**
 * Returned by POST /businesses/register when strict email-verification is on.
 * Notably contains NO JWT or refresh token — the API refuses to issue auth
 * tokens until the owner clicks the link sent to ownerEmail.
 */
export interface RegisterBusinessResponse {
  businessId: string;
  businessSlug: string;
  ownerEmail: string;
  ownerFullName: string;
  requiresEmailVerification: boolean;
  emailDispatched: boolean;
  devVerificationUrl?: string | null;
  message: string;
}

/**
 * Mirrors the C# AuthErrorDto. The SPA branches on `code` to decide whether
 * to show a generic error toast or render a specific recovery action (e.g.
 * resend verification when code === 'EMAIL_NOT_VERIFIED').
 */
export interface AuthError {
  code: string;
  message: string;
  email?: string;
}
