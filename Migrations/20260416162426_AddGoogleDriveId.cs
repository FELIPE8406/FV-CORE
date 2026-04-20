using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FvCore.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDriveId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleDriveId",
                table: "MediaItems",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleDriveId",
                table: "MediaItems");
        }
    }
}
