using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DaVinciTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectName  = table.Column<string>(type: "TEXT", nullable: false),
                    BilledAmount = table.Column<decimal>(type: "TEXT", nullable: true),
                    CreatedAt    = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.ProjectName);
                });

            // Auto-populate from existing sessions so current projects appear immediately.
            migrationBuilder.Sql(@"
                INSERT OR IGNORE INTO Projects (ProjectName, CreatedAt)
                SELECT DISTINCT ProjectName, MIN(StartTime)
                FROM ProjectSessions
                GROUP BY ProjectName;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Projects");
        }
    }
}
