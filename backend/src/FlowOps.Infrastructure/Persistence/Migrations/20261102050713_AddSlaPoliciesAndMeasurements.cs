using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSlaPoliciesAndMeasurements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.CreateTable(
                name: "SlaPolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    WarningBeforeMinutes = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SlaPolicies", x => x.Id);
                    table.CheckConstraint("CK_SlaPolicies_Duration", "[DurationMinutes] >= 1 AND [DurationMinutes] <= 43200");
                    table.CheckConstraint("CK_SlaPolicies_Warning", "[WarningBeforeMinutes] >= 0 AND [WarningBeforeMinutes] < [DurationMinutes]");
                    table.ForeignKey(
                        name: "FK_SlaPolicies_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemSlas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PolicyName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    DurationMinutes = table.Column<int>(type: "int", nullable: false),
                    WarningBeforeMinutes = table.Column<int>(type: "int", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeadlineAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarningAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarnedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    BreachedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemSlas", x => x.Id);
                    table.CheckConstraint("CK_WorkItemSlas_Deadline", "[DeadlineAtUtc] > [StartedAtUtc]");
                    table.CheckConstraint("CK_WorkItemSlas_Duration", "[DurationMinutes] >= 1 AND [DurationMinutes] <= 43200");
                    table.CheckConstraint("CK_WorkItemSlas_Warning", "[WarningBeforeMinutes] >= 0 AND [WarningBeforeMinutes] < [DurationMinutes]");
                    table.ForeignKey(
                        name: "FK_WorkItemSlas_SlaPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "SlaPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkItemSlas_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SlaPolicies_OrganizationId_NormalizedName",
                table: "SlaPolicies",
                columns: new[] { "OrganizationId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemSlas_CompletedAtUtc_BreachedAtUtc_DeadlineAtUtc",
                table: "WorkItemSlas",
                columns: new[] { "CompletedAtUtc", "BreachedAtUtc", "DeadlineAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemSlas_CompletedAtUtc_WarnedAtUtc_WarningAtUtc",
                table: "WorkItemSlas",
                columns: new[] { "CompletedAtUtc", "WarnedAtUtc", "WarningAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemSlas_PolicyId",
                table: "WorkItemSlas",
                column: "PolicyId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemSlas_WorkItemId",
                table: "WorkItemSlas",
                column: "WorkItemId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never replace system actors with a fake user or delete immutable audit history on downgrade.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [AuditEvents] WHERE [ActorUserId] IS NULL)
                BEGIN
                    ;THROW 50001, 'Cannot downgrade SLA schema while system audit events exist. Preserve history and use a forward repair migration.', 1;
                END;
                """);
            migrationBuilder.DropTable(
                name: "WorkItemSlas");

            migrationBuilder.DropTable(
                name: "SlaPolicies");

            migrationBuilder.AlterColumn<Guid>(
                name: "ActorUserId",
                table: "AuditEvents",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
