using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PortfolioApi.Migrations
{
    /// <inheritdoc />
    public partial class AddPostTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Backfill existing rows with an empty array so the NOT NULL
            // ADD COLUMN doesn't reject the table on a populated DB
            // (Postgres 23502: "column 'Tags' contains null values").
            // EF didn't emit a default for the text[] mapping by default,
            // hand-supply one. New rows will overwrite this when the user
            // sets Tags via posts.create / posts.update.
            migrationBuilder.AddColumn<List<string>>(
                name: "Tags",
                table: "Posts",
                type: "text[]",
                nullable: false,
                defaultValue: new List<string>());

            migrationBuilder.CreateIndex(
                name: "IX_Posts_Tags",
                table: "Posts",
                column: "Tags")
                .Annotation("Npgsql:IndexMethod", "gin");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Posts_Tags",
                table: "Posts");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Posts");
        }
    }
}
