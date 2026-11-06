using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessingToggles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // defaultValue: true -- existing installs upgrading into this migration must keep behaving
            // exactly as before (copy/rename/tag into the library) until an admin opts out in Settings.
            migrationBuilder.AddColumn<bool>(
                name: "EnableConversion",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableMoving",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnableRenaming",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableConversion",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "EnableMoving",
                table: "Settings");

            migrationBuilder.DropColumn(
                name: "EnableRenaming",
                table: "Settings");
        }
    }
}
