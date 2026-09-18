using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <inheritdoc />
    public partial class SigningKeyMoreChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SigningKeys_CreatedAt",
                table: "SigningKeys",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningKeys_CreatedAt",
                table: "SigningKeys");
        }
    }
}
