// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  The schema operations here were produced by the Entity Framework Core
//  migrations tool and then reviewed; any hand-written SQL in this file is
//  own work. Full entries: docs/references.md.
//
//  Tooling and documentation:
//    - Microsoft (2026) Entity Framework Core documentation.
// =============================================================================

using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheeBuhle.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// One-time codes for invitations and password resets.
    /// </summary>
    /// <remarks>
    /// Additive only: one new table, nothing existing changes, so it is safe to
    /// apply ahead of the code that uses it. Stores a SHA-256 hash of each code,
    /// never the code. The two check constraints keep the attempt counter
    /// non-negative and every code's expiry after its creation.
    /// </remarks>
    public partial class AddAccountCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "account_codes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    purpose = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    failed_attempts = table.Column<int>(type: "int", nullable: false),
                    issued_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_account_codes", x => x.id);
                    table.CheckConstraint("ck_account_codes_attempts", "[failed_attempts] >= 0");
                    table.CheckConstraint("ck_account_codes_expiry", "[expires_at] > [created_at]");
                    table.ForeignKey(
                        name: "FK_account_codes_users_issued_by",
                        column: x => x.issued_by,
                        principalTable: "users",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_account_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_account_codes_issued_by",
                table: "account_codes",
                column: "issued_by");

            migrationBuilder.CreateIndex(
                name: "ix_account_codes_user",
                table: "account_codes",
                columns: new[] { "user_id", "used_at", "revoked_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_codes");
        }
    }
}
