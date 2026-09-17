using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <inheritdoc />
    public partial class portandmanyothers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Port",
                table: "LoginAttempts",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Port",
                table: "LoginAttempts");
        }
    }
}
