using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSnatchedStatusAndDownloadProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DownloadClientName",
                table: "BookRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DownloadProgressPercent",
                table: "BookRequests",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DownloadSizeBytes",
                table: "BookRequests",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DownloadSizeLeftBytes",
                table: "BookRequests",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SnatchedAt",
                table: "BookRequests",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownloadClientName",
                table: "BookRequests");

            migrationBuilder.DropColumn(
                name: "DownloadProgressPercent",
                table: "BookRequests");

            migrationBuilder.DropColumn(
                name: "DownloadSizeBytes",
                table: "BookRequests");

            migrationBuilder.DropColumn(
                name: "DownloadSizeLeftBytes",
                table: "BookRequests");

            migrationBuilder.DropColumn(
                name: "SnatchedAt",
                table: "BookRequests");
        }
    }
}
