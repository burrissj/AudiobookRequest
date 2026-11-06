using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRatingsTagsBookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BookBookmarks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    JellyfinItemId = table.Column<string>(type: "TEXT", nullable: false),
                    PositionSeconds = table.Column<double>(type: "REAL", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true),
                    Label = table.Column<string>(type: "TEXT", nullable: true),
                    ClientId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookBookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookBookmarks_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookRatings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    JellyfinItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookRatings_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BookTags",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    JellyfinItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Tag = table.Column<string>(type: "TEXT", nullable: false),
                    TagKey = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BookTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BookTags_Users_AppUserId",
                        column: x => x.AppUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BookBookmarks_AppUserId_ClientId",
                table: "BookBookmarks",
                columns: new[] { "AppUserId", "ClientId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookBookmarks_AppUserId_JellyfinItemId",
                table: "BookBookmarks",
                columns: new[] { "AppUserId", "JellyfinItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_BookRatings_AppUserId_JellyfinItemId",
                table: "BookRatings",
                columns: new[] { "AppUserId", "JellyfinItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookTags_AppUserId_JellyfinItemId_TagKey",
                table: "BookTags",
                columns: new[] { "AppUserId", "JellyfinItemId", "TagKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BookTags_AppUserId_TagKey",
                table: "BookTags",
                columns: new[] { "AppUserId", "TagKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BookBookmarks");

            migrationBuilder.DropTable(
                name: "BookRatings");

            migrationBuilder.DropTable(
                name: "BookTags");
        }
    }
}
