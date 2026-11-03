using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReleasesAndEngineeringDashboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Resource",
                table: "Notifications");

            migrationBuilder.AddColumn<DateTime>(
                name: "ResolvedAtUtc",
                table: "WorkItems",
                type: "datetime2",
                nullable: true);

            // Historical throughput uses recorded resolution events, never a mutable update date.
            migrationBuilder.Sql("""
                UPDATE w SET ResolvedAtUtc = resolved.ResolvedAtUtc
                FROM WorkItems w
                CROSS APPLY (
                    SELECT MAX(a.OccurredAtUtc) AS ResolvedAtUtc
                    FROM AuditEvents a
                    WHERE a.EntityId = w.Id AND a.EntityType = 'WorkItem'
                      AND a.Action = 'WorkItem.StatusChanged'
                      AND JSON_VALUE(CASE WHEN ISJSON(a.DetailsJson) = 1 THEN a.DetailsJson ELSE '{}' END, '$.to') = 'Resolved'
                ) resolved
                WHERE w.Status IN ('Resolved', 'Closed');
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ReleaseId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "DetailsJson",
                table: "AuditEvents",
                type: "nvarchar(max)",
                maxLength: 65536,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "ReleaseEnvironments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    NormalizedName = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseEnvironments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReleaseEnvironments_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Releases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ReleaseVersion = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeploymentStartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Releases", x => x.Id);
                    table.CheckConstraint("CK_Releases_Approval", "[Status] = 'Draft' OR ([ApprovedAtUtc] IS NOT NULL AND [ApprovedByUserId] IS NOT NULL AND [ApprovedAtUtc] >= [CreatedAtUtc])");
                    table.CheckConstraint("CK_Releases_Completed", "([Status] IN ('Successful','Failed') AND [CompletedAtUtc] IS NOT NULL AND [Outcome] IS NOT NULL AND [CompletedAtUtc] >= [DeploymentStartedAtUtc]) OR ([Status] NOT IN ('Successful','Failed') AND [CompletedAtUtc] IS NULL)");
                    table.CheckConstraint("CK_Releases_Started", "[Status] NOT IN ('Deploying','Successful','Failed') OR ([DeploymentStartedAtUtc] IS NOT NULL AND [DeploymentStartedAtUtc] >= [ApprovedAtUtc])");
                    table.CheckConstraint("CK_Releases_Status", "[Status] IN ('Draft','Approved','Deploying','Successful','Failed')");
                    table.ForeignKey(
                        name: "FK_Releases_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Releases_ReleaseEnvironments_EnvironmentId",
                        column: x => x.EnvironmentId,
                        principalTable: "ReleaseEnvironments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Releases_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Releases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseIncidents",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseIncidents", x => new { x.ReleaseId, x.IncidentId });
                    table.ForeignKey(
                        name: "FK_ReleaseIncidents_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseIncidents_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReleaseWorkItems",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WorkItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReleaseWorkItems", x => new { x.ReleaseId, x.WorkItemId });
                    table.ForeignKey(
                        name: "FK_ReleaseWorkItems_Releases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "Releases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReleaseWorkItems_WorkItems_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_ReleaseId",
                table: "Notifications",
                column: "ReleaseId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Resource",
                table: "Notifications",
                sql: "([WorkItemId] IS NOT NULL AND [IncidentId] IS NULL AND [ReleaseId] IS NULL) OR ([WorkItemId] IS NULL AND [IncidentId] IS NOT NULL AND [ReleaseId] IS NULL) OR ([WorkItemId] IS NULL AND [IncidentId] IS NULL AND [ReleaseId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseEnvironments_ProjectId_NormalizedName",
                table: "ReleaseEnvironments",
                columns: new[] { "ProjectId", "NormalizedName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseIncidents_IncidentId",
                table: "ReleaseIncidents",
                column: "IncidentId");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ApprovedByUserId",
                table: "Releases",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_CreatedByUserId",
                table: "Releases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_EnvironmentId",
                table: "Releases",
                column: "EnvironmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ProjectId_CreatedAtUtc_Id",
                table: "Releases",
                columns: new[] { "ProjectId", "CreatedAtUtc", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Releases_ProjectId_Status",
                table: "Releases",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ReleaseWorkItems_WorkItemId",
                table: "ReleaseWorkItems",
                column: "WorkItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Releases_ReleaseId",
                table: "Notifications",
                column: "ReleaseId",
                principalTable: "Releases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM Releases) OR EXISTS (SELECT 1 FROM ReleaseEnvironments)
                   OR EXISTS (SELECT 1 FROM AuditEvents WHERE DATALENGTH(DetailsJson) > 4000)
                    THROW 51000, 'Rollback would discard release history or truncate audit records. Restore a reviewed backup instead.', 1;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Releases_ReleaseId",
                table: "Notifications");

            migrationBuilder.DropTable(
                name: "ReleaseIncidents");

            migrationBuilder.DropTable(
                name: "ReleaseWorkItems");

            migrationBuilder.DropTable(
                name: "Releases");

            migrationBuilder.DropTable(
                name: "ReleaseEnvironments");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_ReleaseId",
                table: "Notifications");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Resource",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "ResolvedAtUtc",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ReleaseId",
                table: "Notifications");

            migrationBuilder.AlterColumn<string>(
                name: "DetailsJson",
                table: "AuditEvents",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldMaxLength: 65536,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Resource",
                table: "Notifications",
                sql: "([WorkItemId] IS NOT NULL AND [IncidentId] IS NULL) OR ([WorkItemId] IS NULL AND [IncidentId] IS NOT NULL)");
        }
    }
}
