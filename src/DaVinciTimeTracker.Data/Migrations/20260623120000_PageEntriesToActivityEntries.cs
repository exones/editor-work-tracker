using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DaVinciTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class PageEntriesToActivityEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the new ActivityEntries table with the updated schema.
            // SQLite does not support renaming/dropping columns in-place, so we
            // create the target table, migrate the data, then drop the old table.
            migrationBuilder.CreateTable(
                name: "ActivityEntries",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId   = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: false),
                    UserName    = table.Column<string>(type: "TEXT", nullable: false),
                    ActivityType = table.Column<string>(type: "TEXT", nullable: false),
                    Kind        = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime   = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime     = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FlushedEnd  = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TimelineName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEntries", x => x.Id);
                });

            // Migrate existing data: render entries become Processing/render;
            // all other entries become User/<page-name>.
            migrationBuilder.Sql(@"
                INSERT INTO ActivityEntries
                    (Id, SessionId, ProjectName, UserName, ActivityType, Kind,
                     StartTime, EndTime, FlushedEnd, TimelineName)
                SELECT
                    Id, SessionId, ProjectName, UserName,
                    CASE WHEN IsRendering = 1 THEN 'render' ELSE Page END AS ActivityType,
                    CASE WHEN IsRendering = 1 THEN 'Processing' ELSE 'User' END AS Kind,
                    StartTime, EndTime, FlushedEnd, TimelineName
                FROM PageTimeEntries;
            ");

            // Drop old indexes and table.
            migrationBuilder.DropIndex(
                name: "IX_PageTimeEntries_ProjectName_UserName",
                table: "PageTimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_PageTimeEntries_SessionId",
                table: "PageTimeEntries");

            migrationBuilder.DropTable(
                name: "PageTimeEntries");

            // Recreate indexes on the new table.
            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_ProjectName_UserName",
                table: "ActivityEntries",
                columns: new[] { "ProjectName", "UserName" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_SessionId",
                table: "ActivityEntries",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PageTimeEntries",
                columns: table => new
                {
                    Id          = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId   = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", nullable: false),
                    UserName    = table.Column<string>(type: "TEXT", nullable: false),
                    Page        = table.Column<string>(type: "TEXT", nullable: false),
                    IsRendering = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    StartTime   = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime     = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FlushedEnd  = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TimelineName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PageTimeEntries", x => x.Id);
                });

            migrationBuilder.Sql(@"
                INSERT INTO PageTimeEntries
                    (Id, SessionId, ProjectName, UserName, Page, IsRendering,
                     StartTime, EndTime, FlushedEnd, TimelineName)
                SELECT
                    Id, SessionId, ProjectName, UserName,
                    CASE WHEN Kind = 'Processing' THEN 'deliver' ELSE ActivityType END AS Page,
                    CASE WHEN Kind = 'Processing' THEN 1 ELSE 0 END AS IsRendering,
                    StartTime, EndTime, FlushedEnd, TimelineName
                FROM ActivityEntries;
            ");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEntries_ProjectName_UserName",
                table: "ActivityEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActivityEntries_SessionId",
                table: "ActivityEntries");

            migrationBuilder.DropTable(
                name: "ActivityEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PageTimeEntries_ProjectName_UserName",
                table: "PageTimeEntries",
                columns: new[] { "ProjectName", "UserName" });

            migrationBuilder.CreateIndex(
                name: "IX_PageTimeEntries_SessionId",
                table: "PageTimeEntries",
                column: "SessionId");
        }
    }
}
