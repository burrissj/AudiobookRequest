using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Defaults below mirror AppSettings' C# defaults so an upgrade keeps existing installs behaving
            // as before (notifications on-by-default only matter once a channel is configured).
            migrationBuilder.AddColumn<int>(
                name: "MaxRequestsPerUserPerWeek",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MinFreeDiskGb",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<string>(
                name: "NotifyDiscordWebhookUrl",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyNtfyUrl",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnFailed",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnPendingApproval",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "NotifyOnReady",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyTelegramBotToken",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyTelegramChatId",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NotifyWebhookUrl",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequireApproval",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SmtpFrom",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpHost",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpPassword",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SmtpPort",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 587);

            migrationBuilder.AddColumn<string>(
                name: "SmtpTo",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SmtpUseSsl",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpUsername",
                table: "Settings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WishlistRetryHours",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "Artists",
                table: "LibraryBooks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiagnosticMessage",
                table: "BookRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSearchAt",
                table: "BookRequests",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Wishlisted",
                table: "BookRequests",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxRequestsPerUserPerWeek",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "MinFreeDiskGb",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyDiscordWebhookUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyNtfyUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyOnFailed",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyOnPendingApproval",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyOnReady",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyTelegramBotToken",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyTelegramChatId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "NotifyWebhookUrl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "RequireApproval",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpFrom",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpHost",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpPassword",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpPort",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpTo",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpUseSsl",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "SmtpUsername",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "WishlistRetryHours",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "Artists",
                table: "LibraryBooks");

            migrationBuilder.DropColumn(
                name: "DiagnosticMessage",
                table: "BookRequests");

            migrationBuilder.DropColumn(
                name: "LastSearchAt",
                table: "BookRequests");

            migrationBuilder.DropColumn(
                name: "Wishlisted",
                table: "BookRequests");
        }
    }
}
