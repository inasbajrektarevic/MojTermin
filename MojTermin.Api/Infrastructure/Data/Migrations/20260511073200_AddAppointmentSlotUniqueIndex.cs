using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MojTermin.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentSlotUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Appointments_BusinessId_AppointmentDate_StartTime",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Slot_Active",
                table: "Appointments",
                columns: new[] { "BusinessId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[Status] <> 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Appointments_Slot_Active",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BusinessId_AppointmentDate_StartTime",
                table: "Appointments",
                columns: new[] { "BusinessId", "AppointmentDate", "StartTime" });
        }
    }
}
