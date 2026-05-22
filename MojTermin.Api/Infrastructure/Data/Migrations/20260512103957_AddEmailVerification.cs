using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MojTermin.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerificationTokenExpiresAtUtc",
                table: "AppUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailVerificationTokenHash",
                table: "AppUsers",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "AppUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmailVerifiedAtUtc",
                table: "AppUsers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppUsers_EmailVerificationTokenHash",
                table: "AppUsers",
                column: "EmailVerificationTokenHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AppUsers_EmailVerificationTokenHash",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerificationTokenExpiresAtUtc",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerificationTokenHash",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "AppUsers");

            migrationBuilder.DropColumn(
                name: "EmailVerifiedAtUtc",
                table: "AppUsers");
        }
    }
}
