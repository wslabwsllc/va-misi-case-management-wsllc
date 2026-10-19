using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class E5_TestResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TestCases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    FullName = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Module = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Story = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Requirements = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    AcceptanceCriteria = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Priority = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Preconditions = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ExpectedResult = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    LastOutcome = table.Column<int>(type: "integer", nullable: true),
                    LastRunId = table.Column<long>(type: "bigint", nullable: true),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestCases", x => x.Id);
                    table.CheckConstraint("CK_TestCase_Key_Hex", "\"Key\" ~ '^[0-9a-f]{64}$'");
                });

            migrationBuilder.CreateTable(
                name: "TestEnvironments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    OperatingSystem = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Runtime = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    BaseUrl = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestEnvironments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestResultAuditEntries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ActorId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Actor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: true),
                    ArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    Details = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestResultAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TestRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Runner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    GitBranch = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GitCommit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BuildId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ImageVersion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    EnvironmentId = table.Column<long>(type: "bigint", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Passed = table.Column<int>(type: "integer", nullable: false),
                    Failed = table.Column<int>(type: "integer", nullable: false),
                    Skipped = table.Column<int>(type: "integer", nullable: false),
                    Blocked = table.Column<int>(type: "integer", nullable: false),
                    NotExecuted = table.Column<int>(type: "integer", nullable: false),
                    SuitesJson = table.Column<string>(type: "jsonb", nullable: false),
                    Notes = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    SourcePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ImportedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ImportedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestRuns", x => x.Id);
                    table.CheckConstraint("CK_TestRun_Counts_NonNegative", "\"Total\" >= 0 AND \"Passed\" >= 0 AND \"Failed\" >= 0 AND \"Skipped\" >= 0 AND \"Blocked\" >= 0 AND \"NotExecuted\" >= 0");
                    table.CheckConstraint("CK_TestRun_Status", "\"Status\" BETWEEN 0 AND 2");
                    table.ForeignKey(
                        name: "FK_TestRuns_TestEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "TestEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestExecutions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    TestCaseId = table.Column<long>(type: "bigint", nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FinishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Browser = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Viewport = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ActualResult = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    StackTrace = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true),
                    StandardOutput = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true),
                    ConsoleLog = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true),
                    NetworkLog = table.Column<string>(type: "character varying(64000)", maxLength: 64000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestExecutions", x => x.Id);
                    table.CheckConstraint("CK_TestExecution_Outcome", "\"Outcome\" BETWEEN 0 AND 4");
                    table.ForeignKey(
                        name: "FK_TestExecutions_TestCases_TestCaseId",
                        column: x => x.TestCaseId,
                        principalTable: "TestCases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestExecutions_TestRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RunId = table.Column<long>(type: "bigint", nullable: false),
                    ExecutionId = table.Column<long>(type: "bigint", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RelativePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Caption = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Purged = table.Column<bool>(type: "boolean", nullable: false),
                    PurgedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestArtifacts", x => x.Id);
                    table.CheckConstraint("CK_TestArtifact_Size", "\"SizeBytes\" >= 0");
                    table.ForeignKey(
                        name: "FK_TestArtifacts_TestExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "TestExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TestArtifacts_TestRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "TestRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TestStepResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExecutionId = table.Column<long>(type: "bigint", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Outcome = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    Expected = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Actual = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true),
                    Message = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestStepResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestStepResults_TestExecutions_ExecutionId",
                        column: x => x.ExecutionId,
                        principalTable: "TestExecutions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TestArtifacts_ExecutionId",
                table: "TestArtifacts",
                column: "ExecutionId");

            migrationBuilder.CreateIndex(
                name: "IX_TestArtifacts_RunId_Kind",
                table: "TestArtifacts",
                columns: new[] { "RunId", "Kind" });

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_Key",
                table: "TestCases",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestCases_Module_Type",
                table: "TestCases",
                columns: new[] { "Module", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_TestEnvironments_Key",
                table: "TestEnvironments",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_RunId_Outcome",
                table: "TestExecutions",
                columns: new[] { "RunId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_RunId_TestCaseId",
                table: "TestExecutions",
                columns: new[] { "RunId", "TestCaseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestExecutions_TestCaseId_RunId",
                table: "TestExecutions",
                columns: new[] { "TestCaseId", "RunId" });

            migrationBuilder.CreateIndex(
                name: "IX_TestResultAuditEntries_OccurredAtUtc",
                table: "TestResultAuditEntries",
                column: "OccurredAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_EnvironmentId",
                table: "TestRuns",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_RunKey",
                table: "TestRuns",
                column: "RunKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TestRuns_StartedAtUtc",
                table: "TestRuns",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_TestStepResults_ExecutionId_Order",
                table: "TestStepResults",
                columns: new[] { "ExecutionId", "Order" },
                unique: true);

            // Execution history (read-only): every execution with its run and test case; mapped by TestExecutionHistoryRow.
            migrationBuilder.Sql("""
                CREATE VIEW "TestExecutionHistory" AS
                SELECT e."Id" AS "ExecutionId", c."Id" AS "TestCaseId", c."Title" AS "TestTitle", c."Module", c."Type",
                       r."Id" AS "RunId", r."RunKey", r."StartedAtUtc" AS "RunStartedAtUtc", r."GitCommit", r."GitBranch",
                       e."Outcome", e."DurationMs"
                FROM "TestExecutions" e
                JOIN "TestRuns" r ON r."Id" = e."RunId"
                JOIN "TestCases" c ON c."Id" = e."TestCaseId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP VIEW IF EXISTS "TestExecutionHistory";""");

            migrationBuilder.DropTable(
                name: "TestArtifacts");

            migrationBuilder.DropTable(
                name: "TestResultAuditEntries");

            migrationBuilder.DropTable(
                name: "TestStepResults");

            migrationBuilder.DropTable(
                name: "TestExecutions");

            migrationBuilder.DropTable(
                name: "TestCases");

            migrationBuilder.DropTable(
                name: "TestRuns");

            migrationBuilder.DropTable(
                name: "TestEnvironments");
        }
    }
}
