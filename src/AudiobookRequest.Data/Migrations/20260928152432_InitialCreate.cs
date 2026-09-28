using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SetupCompleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReadarrUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ReadarrApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    ReadarrRootFolderPath = table.Column<string>(type: "TEXT", nullable: true),
                    ReadarrQualityProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    ReadarrMetadataProfileId = table.Column<int>(type: "INTEGER", nullable: true),
                    JellyfinUrl = table.Column<string>(type: "TEXT", nullable: true),
                    JellyfinApiKey = table.Column<string>(type: "TEXT", nullable: true),
                    StagingPath = table.Column<string>(type: "TEXT", nullable: true),
                    LibraryPath = table.Column<string>(type: "TEXT", nullable: true),
                    ScanIntervalMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalRetentionDays = table.Column<int>(type: "INTEGER", nullable: false),
                    NamingPattern = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    JellyfinUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BookRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RequestedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: false),
                    Series = table.Column<string>(type: "TEXT", nullable: true),
                    CoverUrl = table.Column<string>(type: "TEXT", nullable: true),
                    ReadarrForeignBookId = table.Column<string>(type: "TEXT", nullable: false),
                    ReadarrBookId = table.Column<int>(type: "INTEGER", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    FailureReason = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ReadyAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ListeningHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    JellyfinItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", nullable: true),
                    Series = table.Column<string>(type: "TEXT", nullable: true),
                    Genres = table.Column<string>(type: "TEXT", nullable: true),
                    LastPlayedDate = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    PlayCount = table.Column<int>(type: "INTEGER", nullable: false),
                    RefreshedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListeningHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ListeningHistory_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedBooks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookRequestId = table.Column<int>(type: "INTEGER", nullable: true),
                    FinalPath = table.Column<string>(type: "TEXT", nullable: false),
                    OriginalSourcePathsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CopiedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OriginalCleanupAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    OriginalDeleted = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedBooks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedBooks_BookRequests_BookRequestId",
                        column: x => x.BookRequestId,
                        principalTable: "BookRequests",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookRequests_RequestedByUserId",
                table: "BookRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ListeningHistory_AppUserId_JellyfinItemId",
                table: "ListeningHistory",
                columns: new[] { "AppUserId", "JellyfinItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedBooks_BookRequestId",
                table: "ProcessedBooks",
                column: "BookRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_JellyfinUserId",
                table: "Users",
                column: "JellyfinUserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ListeningHistory");

            migrationBuilder.DropTable(
                name: "ProcessedBooks");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "BookRequests");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
