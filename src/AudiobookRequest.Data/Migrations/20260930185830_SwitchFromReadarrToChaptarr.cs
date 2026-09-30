using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class SwitchFromReadarrToChaptarr : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadarrMetadataProfileId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReadarrQualityProfileId",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "ReadarrForeignAuthorId",
                table: "BookRequests");

            migrationBuilder.RenameColumn(
                name: "ReadarrUrl",
                table: "Settings",
                newName: "ChaptarrUrl");

            migrationBuilder.RenameColumn(
                name: "ReadarrRootFolderPath",
                table: "Settings",
                newName: "ChaptarrRootFolderPath");

            migrationBuilder.RenameColumn(
                name: "ReadarrApiKey",
                table: "Settings",
                newName: "ChaptarrApiKey");

            migrationBuilder.RenameColumn(
                name: "ReadarrForeignBookId",
                table: "BookRequests",
                newName: "ChaptarrForeignBookId");

            migrationBuilder.RenameColumn(
                name: "ReadarrBookId",
                table: "BookRequests",
                newName: "ChaptarrBookId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ChaptarrUrl",
                table: "Settings",
                newName: "ReadarrUrl");

            migrationBuilder.RenameColumn(
                name: "ChaptarrRootFolderPath",
                table: "Settings",
                newName: "ReadarrRootFolderPath");

            migrationBuilder.RenameColumn(
                name: "ChaptarrApiKey",
                table: "Settings",
                newName: "ReadarrApiKey");

            migrationBuilder.RenameColumn(
                name: "ChaptarrForeignBookId",
                table: "BookRequests",
                newName: "ReadarrForeignBookId");

            migrationBuilder.RenameColumn(
                name: "ChaptarrBookId",
                table: "BookRequests",
                newName: "ReadarrBookId");

            migrationBuilder.AddColumn<int>(
                name: "ReadarrMetadataProfileId",
                table: "Settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReadarrQualityProfileId",
                table: "Settings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReadarrForeignAuthorId",
                table: "BookRequests",
                type: "TEXT",
                nullable: true);
        }
    }
}
