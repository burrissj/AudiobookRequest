using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeReadarrForeignAuthorIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReadarrForeignAuthorId",
                table: "BookRequests",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "TEXT");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ReadarrForeignAuthorId",
                table: "BookRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "TEXT",
                oldNullable: true);
        }
    }
}
