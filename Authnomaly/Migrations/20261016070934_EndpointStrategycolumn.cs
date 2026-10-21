using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Authnomaly.Migrations
{
    /// <inheritdoc />
    public partial class EndpointStrategycolumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Strategy",
                table: "EndpointsRateLimitStats",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Strategy",
                table: "EndpointsRateLimitStats");
        }
    }
}
