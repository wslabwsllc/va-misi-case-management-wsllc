using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class E2_Cases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    VeteranName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    VeteranAddress = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    VeteranSsnLast4 = table.Column<string>(type: "character(4)", fixedLength: true, maxLength: 4, nullable: true),
                    SeedKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cases", x => x.Id);
                    table.CheckConstraint("CK_Case_Id_Positive", "\"Id\" > 0");
                    table.CheckConstraint("CK_Case_Status_Range", "\"Status\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_Case_Title_Length", "char_length(\"Title\") >= 5");
                    table.CheckConstraint("CK_Case_Veteran_Complete", "(\"VeteranName\" IS NULL) = (\"VeteranAddress\" IS NULL) AND (\"VeteranName\" IS NULL) = (\"VeteranSsnLast4\" IS NULL)");
                    table.CheckConstraint("CK_Case_VeteranSsnLast4_Digits", "\"VeteranSsnLast4\" IS NULL OR \"VeteranSsnLast4\" ~ '^[0-9]{4}$'");
                });

            migrationBuilder.CreateTable(
                name: "CaseAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    CaseId = table.Column<long>(type: "bigint", nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Actor = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    FromStatus = table.Column<int>(type: "integer", nullable: true),
                    ToStatus = table.Column<int>(type: "integer", nullable: true),
                    Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseAuditEntries", x => x.Id);
                    table.CheckConstraint("CK_CaseAuditEntry_FromStatus_Range", "\"FromStatus\" IS NULL OR \"FromStatus\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_CaseAuditEntry_ToStatus_Range", "\"ToStatus\" IS NULL OR \"ToStatus\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_CaseAuditEntries_Cases_CaseId",
                        column: x => x.CaseId,
                        principalTable: "Cases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CaseAuditEntries_CaseId_OccurredAtUtc_Id",
                table: "CaseAuditEntries",
                columns: new[] { "CaseId", "OccurredAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_CreatedAtUtc",
                table: "Cases",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_SeedKey",
                table: "Cases",
                column: "SeedKey",
                unique: true,
                filter: "\"SeedKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Status",
                table: "Cases",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_UpdatedAtUtc",
                table: "Cases",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseAuditEntries");

            migrationBuilder.DropTable(
                name: "Cases");
        }
    }
}
