using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFollowsBackupsAutoFix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults mirror AppSettings: existing installs keep daily backups (14 kept) and new-release alerts on,
            // while auto-fixing imports stays off until an admin opts in.
            migrationBuilder.AddColumn<bool>(
                name: "AutoFixImports",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "BackupRetentionCount",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnNewRelease",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AutoFixAttemptedAt",
                table: "BookRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Follows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    SeriesAsin = table.Column<string>(type: "TEXT", nullable: true),
                    SeriesAuthor = table.Column<string>(type: "TEXT", nullable: true),
                    AutoRequest = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    KnownAsinsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Follows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Follows_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FollowFinds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FollowId = table.Column<int>(type: "INTEGER", nullable: false),
                    Asin = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    ReleaseDate = table.Column<string>(type: "TEXT", nullable: true),
                    FoundAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Dismissed = table.Column<bool>(type: "INTEGER", nullable: false),
                    AutoRequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowFinds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowFinds_Follows_FollowId",
                        column: x => x.FollowId,
                        principalTable: "Follows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FollowFinds_FollowId_Asin",
                table: "FollowFinds",
                columns: new[] { "FollowId", "Asin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Follows_AppUserId_Kind_Name",
                table: "Follows",
                columns: new[] { "AppUserId", "Kind", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FollowFinds");

            migrationBuilder.DropTable(
                name: "Follows");

            migrationBuilder.DropColumn(
                name: "AutoFixImports",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "BackupRetentionCount",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyOnNewRelease",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "AutoFixAttemptedAt",
                table: "BookRequests");
        }
    }
}
