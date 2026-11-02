using System;
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable
namespace FlowOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncidentManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "WorkItemId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");
            migrationBuilder.AddColumn<Guid>(
                name: "IncidentId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: true);
            migrationBuilder.CreateTable(
                name: "Incidents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: true),
                    AffectedService = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Severity = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ReporterUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedByUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    RootCause = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Incidents", x => x.Id);
                    table.CheckConstraint("CK_Incidents_Owner", "[Status] NOT IN ('Investigating','Mitigated') OR [OwnerUserId] IS NOT NULL");
                    table.CheckConstraint("CK_Incidents_Resolution", "([Status] NOT IN ('Resolved','Closed') AND [EndedAtUtc] IS NULL) OR ([Status] IN ('Resolved','Closed') AND [EndedAtUtc] IS NOT NULL AND [EndedAtUtc] >= [StartedAtUtc] AND [Resolution] IS NOT NULL AND [ResolvedByUserId] IS NOT NULL)");
                    table.CheckConstraint("CK_Incidents_Severity", "[Severity] IN ('Low','Medium','High','Critical')");
                    table.CheckConstraint("CK_Incidents_Status", "[Status] IN ('Open','Investigating','Mitigated','Resolved','Closed')");
                    table.ForeignKey(
                        name: "FK_Incidents_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incidents_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incidents_Users_ReporterUserId",
                        column: x => x.ReporterUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Incidents_Users_ResolvedByUserId",
                        column: x => x.ResolvedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "IncidentComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AuthorUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IncidentComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IncidentComments_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidentComments_Users_AuthorUserId",
                        column: x => x.AuthorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateTable(
                name: "IncidentSlas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IncidentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    table.PrimaryKey("PK_IncidentSlas", x => x.Id);
                    table.CheckConstraint("CK_IncidentSlas_Deadline", "[DeadlineAtUtc] > [StartedAtUtc]");
                    table.CheckConstraint("CK_IncidentSlas_Duration", "[DurationMinutes] >= 1 AND [DurationMinutes] <= 43200");
                    table.CheckConstraint("CK_IncidentSlas_Warning", "[WarningBeforeMinutes] >= 0 AND [WarningBeforeMinutes] < [DurationMinutes]");
                    table.ForeignKey(
                        name: "FK_IncidentSlas_Incidents_IncidentId",
                        column: x => x.IncidentId,
                        principalTable: "Incidents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IncidentSlas_SlaPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalTable: "SlaPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });
            migrationBuilder.CreateIndex(
                name: "IX_Notifications_IncidentId",
                table: "Notifications",
                column: "IncidentId");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Notifications_Resource",
                table: "Notifications",
                sql: "([WorkItemId] IS NOT NULL AND [IncidentId] IS NULL) OR ([WorkItemId] IS NULL AND [IncidentId] IS NOT NULL)");
            migrationBuilder.CreateIndex(
                name: "IX_IncidentComments_AuthorUserId",
                table: "IncidentComments",
                column: "AuthorUserId");
            migrationBuilder.CreateIndex(
                name: "IX_IncidentComments_IncidentId_CreatedAtUtc_Id",
                table: "IncidentComments",
                columns: new[] { "IncidentId", "CreatedAtUtc", "Id" });
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_OwnerUserId",
                table: "Incidents",
                column: "OwnerUserId");
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ProjectId_Severity",
                table: "Incidents",
                columns: new[] { "ProjectId", "Severity" });
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ProjectId_Status_StartedAtUtc_Id",
                table: "Incidents",
                columns: new[] { "ProjectId", "Status", "StartedAtUtc", "Id" });
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ReporterUserId",
                table: "Incidents",
                column: "ReporterUserId");
            migrationBuilder.CreateIndex(
                name: "IX_Incidents_ResolvedByUserId",
                table: "Incidents",
                column: "ResolvedByUserId");
            migrationBuilder.CreateIndex(
                name: "IX_IncidentSlas_CompletedAtUtc_BreachedAtUtc_DeadlineAtUtc",
                table: "IncidentSlas",
                columns: new[] { "CompletedAtUtc", "BreachedAtUtc", "DeadlineAtUtc" });
            migrationBuilder.CreateIndex(
                name: "IX_IncidentSlas_CompletedAtUtc_WarnedAtUtc_WarningAtUtc",
                table: "IncidentSlas",
                columns: new[] { "CompletedAtUtc", "WarnedAtUtc", "WarningAtUtc" });
            migrationBuilder.CreateIndex(
                name: "IX_IncidentSlas_IncidentId",
                table: "IncidentSlas",
                column: "IncidentId",
                unique: true);
            migrationBuilder.CreateIndex(
                name: "IX_IncidentSlas_PolicyId",
                table: "IncidentSlas",
                column: "PolicyId");
            migrationBuilder.AddForeignKey(
                name: "FK_Notifications_Incidents_IncidentId",
                table: "Notifications",
                column: "IncidentId",
                principalTable: "Incidents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Incidents]) THROW 51000, 'Incident records exist. Use a reviewed forward migration to preserve operational history.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_Notifications_Incidents_IncidentId",
                table: "Notifications");
            migrationBuilder.DropTable(
                name: "IncidentComments");
            migrationBuilder.DropTable(
                name: "IncidentSlas");
            migrationBuilder.DropTable(
                name: "Incidents");
            migrationBuilder.DropIndex(
                name: "IX_Notifications_IncidentId",
                table: "Notifications");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Notifications_Resource",
                table: "Notifications");
            migrationBuilder.DropColumn(
                name: "IncidentId",
                table: "Notifications");
            migrationBuilder.AlterColumn<Guid>(
                name: "WorkItemId",
                table: "Notifications",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
