using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AudiobookRequest.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReadarrForeignAuthorId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReadarrForeignAuthorId",
                table: "BookRequests",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReadarrForeignAuthorId",
                table: "BookRequests");
        }
    }
}
