using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioApi.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNotifyOnComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true so existing accounts get the same opt-in treatment
            // as new ones — the column is added during a deploy, not by user
            // action, so backfilling false would silently demote everyone.
            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnComment",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyOnComment",
                table: "Users");
        }
    }
}
