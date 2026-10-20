using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <summary>
    /// Issue #22 triage fields (priority, category, owner, due date) and the issue #21 notification cursor. Rows that
    /// existed before get values: seeded demo cases the same ones <c>CaseSeedData.TriageFor</c> gives a fresh seed
    /// (so old and new databases match), other cases Medium / General / unassigned and due ten days after creation
    /// (the Medium target). <see cref="BackfillSql"/> is checked against the seed formula by an integration test.
    /// </summary>
    public partial class E5_CaseTriageAndNotifications : Migration
    {
        /// <summary>Fills the triage fields of rows created before this migration.</summary>
        public const string BackfillSql = """
            UPDATE "Cases" AS c SET
                "Priority" = (ARRAY[1, 2, 0, 3, 1])[(s.i % 5) + 1],
                "Category" = (ARRAY[8, 1, 11, 6, 10, 7, 3, 2, 9, 8, 5, 4])[(s.i % 12) + 1],
                "AssignedTo" = CASE s.i % 3 WHEN 0 THEN 'caseworker@example.test' WHEN 1 THEN 'supervisor@example.test' ELSE NULL END,
                "DueOn" = DATE '2026-06-01' + ((s.i * 37) % 420)
            FROM (SELECT "Id", CAST(substring("SeedKey" FROM 6) AS integer) - 1 AS i
                  FROM "Cases" WHERE "SeedKey" ~ '^case-[0-9]+$') AS s
            WHERE c."Id" = s."Id";
            UPDATE "Cases" SET "DueOn" = CAST(("CreatedAtUtc" AT TIME ZONE 'UTC') AS date) + 10
            WHERE "SeedKey" IS NULL OR "SeedKey" !~ '^case-[0-9]+$';
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AssignedTo",
                table: "Cases",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "Cases",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DueOn",
                table: "Cases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "Cases",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.Sql(BackfillSql);

            migrationBuilder.CreateTable(
                name: "CaseNotificationCursors",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    LastReadAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CaseNotificationCursors", x => x.UserId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cases_AssignedTo",
                table: "Cases",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_DueOn",
                table: "Cases",
                column: "DueOn");

            migrationBuilder.CreateIndex(
                name: "IX_Cases_Priority",
                table: "Cases",
                column: "Priority");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Case_Category_Range",
                table: "Cases",
                sql: "\"Category\" BETWEEN 0 AND 11");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Case_Priority_Range",
                table: "Cases",
                sql: "\"Priority\" BETWEEN 0 AND 3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CaseNotificationCursors");

            migrationBuilder.DropIndex(
                name: "IX_Cases_AssignedTo",
                table: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_Cases_DueOn",
                table: "Cases");

            migrationBuilder.DropIndex(
                name: "IX_Cases_Priority",
                table: "Cases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Case_Category_Range",
                table: "Cases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Case_Priority_Range",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "AssignedTo",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "DueOn",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "Cases");
        }
    }
}
