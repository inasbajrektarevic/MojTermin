using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MojTermin.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "Appointments",
                type: "rowversion",
                rowVersion: true,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "Appointments");
        }
    }
}
