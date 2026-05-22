using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MojTermin.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCancelTokenAndReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiresAtUtc",
                table: "AppUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "AppUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CancellationTokenExpiresAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CancellationTokenHash",
                table: "Appointments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Reminder1hSentAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Reminder24hSentAtUtc",
                table: "Appointments",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_PasswordResetTokenHash",
                table: "AppUsers",
                column: "PasswordResetTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CancellationTokenHash",
                table: "Appointments",
                column: "CancellationTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_PasswordResetTokenHash",
                table: "AppUsers");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CancellationTokenHash",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiresAtUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "CancellationTokenExpiresAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CancellationTokenHash",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Reminder1hSentAtUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Reminder24hSentAtUtc",
                table: "Appointments");
        }
    }
}
