using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MojTermin.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffMembersAndAppointmentStaff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_Appointments_Slot_Active",
                table: "Appointments");

            migrationBuilder.AddColumn<Guid>(
                name: "StaffMemberId",
                table: "Appointments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StaffMembers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StaffMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StaffMembers_Businesses_BusinessId",
                        column: x => x.BusinessId,
                        principalTable: "Businesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_StaffMemberId",
                table: "Appointments",
                column: "StaffMemberId");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Slot_Active_ByStaff",
                table: "Appointments",
                columns: new[] { "BusinessId", "StaffMemberId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[Status] <> 3 AND [StaffMemberId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Slot_Active_Unassigned",
                table: "Appointments",
                columns: new[] { "BusinessId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[Status] <> 3 AND [StaffMemberId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StaffMembers_BusinessId_IsActive",
                table: "StaffMembers",
                columns: new[] { "BusinessId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_StaffMembers_StaffMemberId",
                table: "Appointments",
                column: "StaffMemberId",
                principalTable: "StaffMembers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_StaffMembers_StaffMemberId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "StaffMembers");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_StaffMemberId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "UX_Appointments_Slot_Active_ByStaff",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "UX_Appointments_Slot_Active_Unassigned",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "StaffMemberId",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "UX_Appointments_Slot_Active",
                table: "Appointments",
                columns: new[] { "BusinessId", "AppointmentDate", "StartTime" },
                unique: true,
                filter: "[Status] <> 3");
        }
    }
}
