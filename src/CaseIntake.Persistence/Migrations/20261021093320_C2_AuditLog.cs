using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class C2_AuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "TestResultAuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CorrelationId",
                table: "CaseAuditEntries",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ActorName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Outcome = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EntityType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EntityId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    EntityName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Summary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Before = table.Column<string>(type: "jsonb", nullable: true),
                    After = table.Column<string>(type: "jsonb", nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
                    table.CheckConstraint("CK_AuditLogEntry_Outcome", "\"Outcome\" IN ('Success', 'Failure', 'Denied')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseAuditEntries_OccurredAtUtc",
                table: "CaseAuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_ActorId",
                table: "AuditLogEntries",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_CorrelationId",
                table: "AuditLogEntries",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_EntityType_EntityId_OccurredAtUtc",
                table: "AuditLogEntries",
                columns: new[] { "EntityType", "EntityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogEntries_OccurredAtUtc",
                table: "AuditLogEntries",
                column: "OccurredAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogEntries");

            migrationBuilder.DropIndex(
                name: "IX_CaseAuditEntries_OccurredAtUtc",
                table: "CaseAuditEntries");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "TestResultAuditEntries");

            migrationBuilder.DropColumn(
                name: "CorrelationId",
                table: "CaseAuditEntries");
        }
    }
}
