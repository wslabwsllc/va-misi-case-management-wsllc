using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CaseIntake.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CR2_VeteranServiceHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CharacterOfService",
                table: "Cases",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ServiceBranch",
                table: "Cases",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ServiceEndDate",
                table: "Cases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ServiceHistoryStatus",
                table: "Cases",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ServiceRecordVerifiedOn",
                table: "Cases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ServiceStartDate",
                table: "Cases",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Case_ServiceHistory_Complete",
                table: "Cases",
                sql: "CASE WHEN \"ServiceHistoryStatus\" = 0 THEN \"ServiceBranch\" IS NOT NULL AND \"ServiceStartDate\" IS NOT NULL AND \"CharacterOfService\" IS NOT NULL ELSE \"ServiceBranch\" IS NULL AND \"ServiceStartDate\" IS NULL AND \"ServiceEndDate\" IS NULL AND \"CharacterOfService\" IS NULL AND \"ServiceRecordVerifiedOn\" IS NULL END");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Case_ServiceHistory_Dates",
                table: "Cases",
                sql: "\"ServiceEndDate\" IS NULL OR \"ServiceEndDate\" >= \"ServiceStartDate\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Case_ServiceHistory_Range",
                table: "Cases",
                sql: "\"ServiceHistoryStatus\" IS NULL OR \"ServiceHistoryStatus\" BETWEEN 0 AND 2");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Case_ServiceHistory_RequiresVeteran",
                table: "Cases",
                sql: "\"ServiceHistoryStatus\" IS NULL OR \"VeteranName\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Case_ServiceHistory_Complete",
                table: "Cases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Case_ServiceHistory_Dates",
                table: "Cases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Case_ServiceHistory_Range",
                table: "Cases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Case_ServiceHistory_RequiresVeteran",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "CharacterOfService",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceBranch",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceEndDate",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceHistoryStatus",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceRecordVerifiedOn",
                table: "Cases");

            migrationBuilder.DropColumn(
                name: "ServiceStartDate",
                table: "Cases");
        }
    }
}
