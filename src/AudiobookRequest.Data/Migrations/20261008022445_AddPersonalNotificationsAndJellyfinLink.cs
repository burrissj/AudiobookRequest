using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonalNotificationsAndJellyfinLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing users keep getting their own ready/failed/new-release alerts once they add a personal channel.
            migrationBuilder.AddColumn<string>(
                name: "NotifyEmail",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyNtfyTopic",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnFailed",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnNewRelease",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnReady",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyTelegramChatId",
                table: "Users",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JellyfinPublicUrl",
                table: "Settings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotifyEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyNtfyTopic",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyOnFailed",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyOnNewRelease",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyOnReady",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "NotifyTelegramChatId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "JellyfinPublicUrl",
                table: "Settings");
        }
    }
}
