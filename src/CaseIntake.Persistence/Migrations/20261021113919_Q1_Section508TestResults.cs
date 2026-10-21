using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Q1_Section508TestResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TestRun_Counts_NonNegative",
                table: "TestRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TestExecution_Outcome",
                table: "TestExecutions");

            migrationBuilder.AddColumn<int>(
                name: "NotApplicable",
                table: "TestRuns",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AssistiveTechnology",
                table: "TestExecutions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefectReference",
                table: "TestExecutions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemediationNotes",
                table: "TestExecutions",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tester",
                table: "TestExecutions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Accessibility",
                table: "TestCases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Method",
                table: "TestCases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Section508Provision",
                table: "TestCases",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WcagCriterion",
                table: "TestCases",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WcagLevel",
                table: "TestCases",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WcagVersion",
                table: "TestCases",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Workflow",
                table: "TestCases",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_TestRun_Counts_NonNegative",
                table: "TestRuns",
                sql: "\"Total\" >= 0 AND \"Passed\" >= 0 AND \"Failed\" >= 0 AND \"Skipped\" >= 0 AND \"Blocked\" >= 0 AND \"NotExecuted\" >= 0 AND \"NotApplicable\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TestExecution_Outcome",
                table: "TestExecutions",
                sql: "\"Outcome\" BETWEEN 0 AND 5");

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_Accessibility",
                table: "TestCases",
                column: "Accessibility");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_TestRun_Counts_NonNegative",
                table: "TestRuns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_TestExecution_Outcome",
                table: "TestExecutions");

            migrationBuilder.DropIndex(
                name: "IX_TestCases_Accessibility",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "NotApplicable",
                table: "TestRuns");

            migrationBuilder.DropColumn(
                name: "AssistiveTechnology",
                table: "TestExecutions");

            migrationBuilder.DropColumn(
                name: "DefectReference",
                table: "TestExecutions");

            migrationBuilder.DropColumn(
                name: "RemediationNotes",
                table: "TestExecutions");

            migrationBuilder.DropColumn(
                name: "Tester",
                table: "TestExecutions");

            migrationBuilder.DropColumn(
                name: "Accessibility",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Method",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Section508Provision",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "WcagCriterion",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "WcagLevel",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "WcagVersion",
                table: "TestCases");

            migrationBuilder.DropColumn(
                name: "Workflow",
                table: "TestCases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TestRun_Counts_NonNegative",
                table: "TestRuns",
                sql: "\"Total\" >= 0 AND \"Passed\" >= 0 AND \"Failed\" >= 0 AND \"Skipped\" >= 0 AND \"Blocked\" >= 0 AND \"NotExecuted\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_TestExecution_Outcome",
                table: "TestExecutions",
                sql: "\"Outcome\" BETWEEN 0 AND 4");
        }
    }
}
