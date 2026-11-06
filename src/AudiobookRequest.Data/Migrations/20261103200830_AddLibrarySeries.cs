using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLibrarySeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SeriesAsin",
                table: "LibraryBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SeriesCheckedAt",
                table: "LibraryBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesName",
                table: "LibraryBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SeriesSequence",
                table: "LibraryBooks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SeriesAsin",
                table: "LibraryBooks");

            migrationBuilder.DropColumn(
                name: "SeriesCheckedAt",
                table: "LibraryBooks");

            migrationBuilder.DropColumn(
                name: "SeriesName",
                table: "LibraryBooks");

            migrationBuilder.DropColumn(
                name: "SeriesSequence",
                table: "LibraryBooks");
        }
    }
}
