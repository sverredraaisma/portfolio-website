using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioApi.Migrations
{
    /// <inheritdoc />
    public partial class AddSharedLocationPrecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue 3 backfills existing rows with the same rounding
            // they got under the previous global LocationOptions.Public-
            // PrecisionDecimals = 3 policy. Without this, every existing
            // pin would suddenly snap to 0-decimal (~111km region) precision
            // on the next /map render — a privacy-preserving change but a
            // surprising one. Users can pick a different precision later.
            migrationBuilder.AddColumn<int>(
                name: "PrecisionDecimals",
                table: "SharedLocations",
                type: "integer",
                nullable: false,
                defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrecisionDecimals",
                table: "SharedLocations");
        }
    }
}
