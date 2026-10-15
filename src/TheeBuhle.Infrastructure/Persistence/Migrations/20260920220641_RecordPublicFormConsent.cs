// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  The schema operations here were produced by the Entity Framework Core
//  migrations tool and then reviewed; any hand-written SQL in this file is
//  own work. Full entries: docs/references.md.
//
//  Tooling and documentation:
//    - Microsoft (2026) Entity Framework Core documentation.
//    - Republic of South Africa (2013) Protection of Personal Information Act 4 of 2013.
// =============================================================================

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheeBuhle.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records the POPIA consent that the donation and event-registration forms
    /// have always asked for.
    /// </summary>
    /// <remarks>
    /// Note the default of <c>false</c> on both columns, which is deliberate and
    /// should stay that way. Rows written before this migration were created
    /// when the answer was not kept, so backfilling them as <c>true</c> would
    /// record a consent nobody can show was given — which is a worse record than
    /// none at all.
    /// </remarks>
    public partial class RecordPublicFormConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "consent_popia",
                table: "event_registrations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "consent_popia",
                table: "donations",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "consent_popia",
                table: "event_registrations");

            migrationBuilder.DropColumn(
                name: "consent_popia",
                table: "donations");
        }
    }
}
