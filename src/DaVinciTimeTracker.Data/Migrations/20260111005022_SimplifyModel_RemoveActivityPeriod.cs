using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DaVinciTimeTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyModel_RemoveActivityPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityPeriods");

            migrationBuilder.DropColumn(
                name: "TotalActiveSeconds",
                table: "ProjectSessions");

            migrationBuilder.DropColumn(
                name: "TotalElapsedSeconds",
                table: "ProjectSessions");

            migrationBuilder.AddColumn<DateTime>(
                name: "FlushedEnd",
                table: "ProjectSessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FlushedEnd",
                table: "ProjectSessions");

            migrationBuilder.AddColumn<int>(
                name: "TotalActiveSeconds",
                table: "ProjectSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalElapsedSeconds",
                table: "ProjectSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ActivityPeriods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SessionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPeriods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityPeriods_ProjectSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "ProjectSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPeriods_SessionId",
                table: "ActivityPeriods",
                column: "SessionId");
        }
    }
}
