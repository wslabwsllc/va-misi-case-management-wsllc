using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class E6_NotificationReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CaseNotificationReads",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AuditEntryId = table.Column<long>(type: "bigint", nullable: false),
                    ReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseNotificationReads", x => new { x.UserId, x.AuditEntryId });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseNotificationReads");
        }
    }
}
