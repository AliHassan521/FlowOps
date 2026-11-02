using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SerializeRefreshSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RefreshSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LockStamp = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_UserId",
                table: "RefreshSessions",
                column: "UserId");

            // Preserve existing login families before enforcing the new foreign key.
            // An inconsistent legacy family must fail migration rather than grant access.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT [FamilyId] FROM [RefreshTokens] GROUP BY [FamilyId]
                    HAVING COUNT(DISTINCT [UserId]) > 1)
                    THROW 51000, 'A refresh family belongs to multiple users.', 1;
                INSERT INTO [RefreshSessions] ([Id], [UserId], [CreatedAtUtc], [RevokedAtUtc], [LockStamp])
                SELECT [FamilyId], [UserId], MIN([CreatedAtUtc]),
                    CASE WHEN SUM(CASE WHEN [RevokedAtUtc] IS NULL THEN 1 ELSE 0 END) = 0
                        THEN MAX([RevokedAtUtc]) ELSE NULL END, NEWID()
                FROM [RefreshTokens] GROUP BY [FamilyId], [UserId];
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_RefreshTokens_RefreshSessions_FamilyId",
                table: "RefreshTokens",
                column: "FamilyId",
                principalTable: "RefreshSessions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RefreshTokens_RefreshSessions_FamilyId",
                table: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "RefreshSessions");
        }
    }
}
