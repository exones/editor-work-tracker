using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DaVinciTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTimelineAndRenderToPageEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsRendering",
                table: "PageTimeEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TimelineName",
                table: "PageTimeEntries",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsRendering",
                table: "PageTimeEntries");

            migrationBuilder.DropColumn(
                name: "TimelineName",
                table: "PageTimeEntries");
        }
    }
}
