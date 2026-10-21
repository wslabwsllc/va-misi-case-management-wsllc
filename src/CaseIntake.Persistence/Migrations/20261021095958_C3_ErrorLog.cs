using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C3_ErrorLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ErrorLogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Category = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventId = table.Column<int>(type: "integer", nullable: false),
                    EventName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    ExceptionType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StackTrace = table.Column<string>(type: "character varying(16000)", maxLength: 16000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    RequestPath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Fingerprint = table.Column<string>(type: "character(16)", fixedLength: true, maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StatusChangedUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    StatusChangedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    StatusNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ErrorLogEntries", x => x.Id);
                    table.CheckConstraint("CK_ErrorLogEntry_Level", "\"Level\" IN ('Error', 'Critical')");
                    table.CheckConstraint("CK_ErrorLogEntry_Status", "\"Status\" IN ('Open', 'Acknowledged', 'Resolved')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_CorrelationId",
                table: "ErrorLogEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_Fingerprint",
                table: "ErrorLogEntries",
                column: "Fingerprint");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_OccurredAtUtc",
                table: "ErrorLogEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ErrorLogEntries_Status_OccurredAtUtc",
                table: "ErrorLogEntries",
                columns: new[] { "Status", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ErrorLogEntries");
        }
    }
}
