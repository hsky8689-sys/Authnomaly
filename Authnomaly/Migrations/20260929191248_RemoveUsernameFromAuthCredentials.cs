using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUsernameFromAuthCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Username",
                table: "AuthCredentials");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "AuthCredentials",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
