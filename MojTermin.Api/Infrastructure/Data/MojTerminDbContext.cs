using Microsoft.EntityFrameworkCore;
using MojTermin.Api.Domain.Entities;

namespace MojTermin.Api.Infrastructure.Data;

public class MojTerminDbContext(DbContextOptions<MojTerminDbContext> options) : DbContext(options)
{
    public DbSet<Business> Businesses => Set<Business>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<WorkingHour> WorkingHours => Set<WorkingHour>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<StaffMember> StaffMembers => Set<StaffMember>();
    public DbSet<StaffTimeOff> StaffTimeOffs => Set<StaffTimeOff>();
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<AdminAuditLog> AdminAuditLogs => Set<AdminAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Business>(entity =>
        {
            entity.ToTable("Businesses");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Slug).HasMaxLength(160).IsRequired();
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(120);
            entity.Property(x => x.Address).HasMaxLength(250);
            entity.Property(x => x.Description).HasMaxLength(1000);
            entity.Property(x => x.LogoUrl).HasMaxLength(500);
            entity.Property(x => x.CoverImageUrl).HasMaxLength(500);
            entity.Property(x => x.ThemePreset).HasMaxLength(40).HasDefaultValue("default");
            entity.Property(x => x.PrimaryColor).HasMaxLength(20);
            entity.Property(x => x.SecondaryColor).HasMaxLength(20);
        });

        modelBuilder.Entity<Service>(entity =>
        {
            entity.ToTable("Services");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500);
            entity.Property(x => x.ImageUrl).HasMaxLength(500);
            entity.Property(x => x.Price).HasPrecision(18, 2);
            entity.HasIndex(x => new { x.BusinessId, x.Name });
            entity.HasOne(x => x.Business)
                .WithMany(x => x.Services)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkingHour>(entity =>
        {
            entity.ToTable("WorkingHours");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.BusinessId, x.DayOfWeek }).IsUnique();
            entity.HasOne(x => x.Business)
                .WithMany(x => x.WorkingHours)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Client>(entity =>
        {
            entity.ToTable("Clients");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Phone).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(120);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.HasIndex(x => new { x.BusinessId, x.Phone });
            entity.HasOne(x => x.Business)
                .WithMany(x => x.Clients)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffMember>(entity =>
        {
            entity.ToTable("StaffMembers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Title).HasMaxLength(120);
            entity.Property(x => x.Phone).HasMaxLength(30);
            entity.Property(x => x.Email).HasMaxLength(120);
            entity.HasIndex(x => new { x.BusinessId, x.IsActive });
            entity.HasOne(x => x.Business)
                .WithMany(x => x.StaffMembers)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StaffTimeOff>(entity =>
        {
            entity.ToTable("StaffTimeOffs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Reason).HasMaxLength(300);
            entity.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.DateFrom, x.DateTo });
            entity.HasOne(x => x.Business)
                .WithMany(x => x.StaffTimeOffs)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.StaffMember)
                .WithMany(x => x.TimeOffs)
                .HasForeignKey(x => x.StaffMemberId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<Appointment>(entity =>
        {
            entity.ToTable("Appointments");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Note).HasMaxLength(1000);
            entity.Property(x => x.ContactFullName).HasMaxLength(150);
            entity.Property(x => x.ContactEmail).HasMaxLength(320);
            entity.Property(x => x.CancellationTokenHash).HasMaxLength(128);
            // SQL Server rowversion / timestamp column. Maintained by the engine; EF
            // adds it to UPDATE/DELETE WHERE clauses to detect concurrent modifications.
            entity.Property(x => x.RowVersion).IsRowVersion();
            entity.HasIndex(x => new { x.BusinessId, x.AppointmentDate, x.StartTime });
            entity.HasIndex(x => x.CancellationTokenHash);
            entity.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.AppointmentDate, x.StartTime });

            // Slot uniqueness split in two filtered unique indexes:
            // 1) Unassigned appointments (StaffMemberId IS NULL) still allow at most one
            //    active slot per business+date+time.
            // 2) Assigned appointments (StaffMemberId IS NOT NULL) are unique per
            //    business+staff+date+time, enabling concurrent bookings across staff.
            entity.HasIndex(x => new { x.BusinessId, x.AppointmentDate, x.StartTime })
                .HasDatabaseName("UX_Appointments_Slot_Active_Unassigned")
                .IsUnique()
                .HasFilter("[Status] <> 3 AND [StaffMemberId] IS NULL");
            entity.HasIndex(x => new { x.BusinessId, x.StaffMemberId, x.AppointmentDate, x.StartTime })
                .HasDatabaseName("UX_Appointments_Slot_Active_ByStaff")
                .IsUnique()
                .HasFilter("[Status] <> 3 AND [StaffMemberId] IS NOT NULL");

            entity.HasOne(x => x.Business)
                .WithMany(x => x.Appointments)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.NoAction);
            entity.HasOne(x => x.Service)
                .WithMany(x => x.Appointments)
                .HasForeignKey(x => x.ServiceId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Client)
                .WithMany(x => x.Appointments)
                .HasForeignKey(x => x.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.StaffMember)
                .WithMany(x => x.Appointments)
                .HasForeignKey(x => x.StaffMemberId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("AppUsers");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Username).HasMaxLength(80).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(x => x.Role).HasMaxLength(30).IsRequired();
            entity.Property(x => x.EmailVerificationTokenHash).HasMaxLength(128);
            entity.Property(x => x.PasswordResetTokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.Username).IsUnique();
            entity.HasIndex(x => x.EmailVerificationTokenHash);
            entity.HasIndex(x => x.PasswordResetTokenHash);
            entity.HasIndex(x => new { x.BusinessId, x.IsActive });
            entity.HasOne(x => x.Business)
                .WithMany()
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Token).HasMaxLength(300).IsRequired();
            entity.HasIndex(x => x.Token).IsUnique();
            entity.HasIndex(x => new { x.AppUserId, x.ExpiresAtUtc });
            entity.HasOne(x => x.AppUser)
                .WithMany(x => x.RefreshTokens)
                .HasForeignKey(x => x.AppUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.ToTable("NotificationLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Recipient).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(300).IsRequired();
            entity.Property(x => x.BodyPreview).HasMaxLength(1200).IsRequired();
            entity.Property(x => x.ErrorMessage).HasMaxLength(1500);
            entity.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            entity.HasOne(x => x.Business)
                .WithMany(x => x.NotificationLogs)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Appointment)
                .WithMany()
                .HasForeignKey(x => x.AppointmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AdminAuditLog>(entity =>
        {
            entity.ToTable("AdminAuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ActorName).HasMaxLength(150).IsRequired();
            entity.Property(x => x.ActorEmail).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Action).HasMaxLength(60).IsRequired();
            entity.Property(x => x.ResourceType).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Summary).HasMaxLength(700);
            entity.Property(x => x.MetadataJson).HasMaxLength(4000);
            entity.HasIndex(x => new { x.BusinessId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.BusinessId, x.ResourceType, x.Action });
            entity.HasOne(x => x.Business)
                .WithMany(x => x.AdminAuditLogs)
                .HasForeignKey(x => x.BusinessId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
