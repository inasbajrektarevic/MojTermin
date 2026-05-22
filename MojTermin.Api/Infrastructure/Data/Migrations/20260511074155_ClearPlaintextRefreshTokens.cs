using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MojTermin.Api.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClearPlaintextRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Refresh tokens are now stored as SHA-256 digests of the raw client-held value.
            // Any pre-existing rows in this table were stored as plaintext, so a DB-dump
            // would have leaked working sessions. Wipe them. Affected users will be asked
            // to log in again on next request (frontend already handles 401 + login flow).
            migrationBuilder.Sql("DELETE FROM [RefreshTokens];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Irreversible by design — we cannot reconstruct plaintext tokens from hashes.
        }
    }
}
