using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <inheritdoc />
    public partial class PartialUniqueIndexOnSigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SigningKeys_IsCurrent",
                table: "SigningKeys",
                column: "IsCurrent",
                unique: true,
                filter: "\"IsCurrent\"= true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SigningKeys_IsCurrent",
                table: "SigningKeys");
        }
    }
}
