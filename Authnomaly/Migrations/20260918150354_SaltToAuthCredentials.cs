using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <inheritdoc />
    public partial class SaltToAuthCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Password",
                table: "AuthCredentials",
                newName: "PasswordHash");

            migrationBuilder.AddColumn<byte[]>(
                name: "Salt",
                table: "AuthCredentials",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Salt",
                table: "AuthCredentials");

            migrationBuilder.RenameColumn(
                name: "PasswordHash",
                table: "AuthCredentials",
                newName: "Password");
        }
    }
}
