using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FlowOps.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokenFamilies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: true);

            // Preserve the existing rotation graph: every descendant keeps the
            // family ID of the first token in its chain. Tokens whose historical
            // predecessor is missing become the root of their remaining chain.
            migrationBuilder.Sql(
                """
                ;WITH TokenFamilies AS
                (
                    SELECT token.[Id], token.[TokenHash], token.[ReplacedByTokenHash], token.[Id] AS [FamilyId]
                    FROM [RefreshTokens] AS token
                    WHERE NOT EXISTS
                    (
                        SELECT 1
                        FROM [RefreshTokens] AS previous
                        WHERE previous.[ReplacedByTokenHash] = token.[TokenHash]
                    )

                    UNION ALL

                    SELECT nextToken.[Id], nextToken.[TokenHash], nextToken.[ReplacedByTokenHash], family.[FamilyId]
                    FROM [RefreshTokens] AS nextToken
                    INNER JOIN TokenFamilies AS family
                        ON nextToken.[TokenHash] = family.[ReplacedByTokenHash]
                )
                UPDATE token
                SET [FamilyId] = family.[FamilyId]
                FROM [RefreshTokens] AS token
                INNER JOIN TokenFamilies AS family ON family.[Id] = token.[Id]
                OPTION (MAXRECURSION 32767);
                """);

            // Defensive fallback for isolated or cyclic historical data; no
            // existing token should be grouped with an unrelated session.
            migrationBuilder.Sql(
                "UPDATE [RefreshTokens] SET [FamilyId] = [Id] WHERE [FamilyId] IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "FamilyId",
                table: "RefreshTokens",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_FamilyId_RevokedAtUtc",
                table: "RefreshTokens",
                columns: new[] { "FamilyId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RefreshTokens_FamilyId_RevokedAtUtc",
                table: "RefreshTokens");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "RefreshTokens");
        }
    }
}
