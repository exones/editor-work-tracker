using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DaVinciTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserNameToSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UserName",
                table: "ProjectSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectSessions_UserName",
                table: "ProjectSessions",
                column: "UserName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProjectSessions_UserName",
                table: "ProjectSessions");

            migrationBuilder.DropColumn(
                name: "UserName",
                table: "ProjectSessions");
        }
    }
}
