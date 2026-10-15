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

using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TheeBuhle.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Makes three promises the documentation already made true in the database.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <strong>Every "who did this" column references a real user.</strong>
    /// Thirteen of them had no foreign key, and the development seeder had
    /// written an invented identifier into every attendance mark and school
    /// result. Existing rows are repaired first — see below — then constrained.
    /// </para>
    /// <para>
    /// <strong>Consent history survives its form.</strong> Deleting a permission
    /// form used to cascade and delete every parent's decision on it.
    /// </para>
    /// <para>
    /// <strong>The audit log is immutable and consents are append-only.</strong>
    /// The PHP prototype enforced both with triggers; the .NET translation had
    /// dropped them, while the staff console and the privacy policy still told
    /// people the database refused the change. These are the SQL Server
    /// equivalents.
    /// </para>
    /// <para>
    /// The repair touches only rows whose reference points at nobody, which on a
    /// production database is none: the seeder that wrote them runs in
    /// development only. A nullable reference that points at nobody becomes
    /// NULL — "unknown" is the honest value. A required one is attributed to the
    /// earliest administrator, because the column cannot be empty; if there is
    /// no administrator the migration stops rather than inventing one.
    /// </para>
    /// </remarks>
    public partial class EnforceRecordIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- repair, before any constraint is added -----------------------

            foreach (var (table, column) in new[]
            {
                ("audit_log", "user_id"), ("buddy_requests", "approved_by"),
                ("check_ins", "closed_by"), ("check_ins", "escalated_to"),
                ("compliance_documents", "verified_by"), ("contact_messages", "handled_by"),
                ("volunteer_applications", "reviewed_by"),
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE [{table}] SET [{column}] = NULL "
                    + $"WHERE [{column}] IS NOT NULL AND NOT EXISTS (SELECT 1 FROM [users] u WHERE u.[id] = [{table}].[{column}]);");
            }

            var required = new[]
            {
                ("announcements", "published_by"), ("assessment_results", "captured_by"),
                ("attendance", "recorded_by"), ("mentor_assignments", "approved_by"),
                ("peer_groups", "approved_by"), ("permission_forms", "created_by"),
            };

            var dangling = string.Join(" OR ", required.Select(pair =>
                $"EXISTS (SELECT 1 FROM [{pair.Item1}] t WHERE NOT EXISTS (SELECT 1 FROM [users] u WHERE u.[id] = t.[{pair.Item2}]))"));

            var fixes = string.Join(" ", required.Select(pair =>
                $"UPDATE [{pair.Item1}] SET [{pair.Item2}] = @admin "
                + $"WHERE NOT EXISTS (SELECT 1 FROM [users] u WHERE u.[id] = [{pair.Item1}].[{pair.Item2}]);"));

            migrationBuilder.Sql(
                "DECLARE @admin uniqueidentifier = (SELECT TOP 1 [id] FROM [users] WHERE [role] = 'Admin' ORDER BY [created_at]); "
                + $"IF ({dangling}) BEGIN "
                + "IF @admin IS NULL THROW 51000, 'Required actor references point at no user and there is no administrator to attribute them to. Repair them by hand before migrating.', 1; "
                + fixes
                + " END");

            migrationBuilder.DropForeignKey(
                name: "FK_consents_permission_forms_form_id",
                table: "consents");

            migrationBuilder.CreateIndex(
                name: "IX_volunteer_applications_reviewed_by",
                table: "volunteer_applications",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "IX_permission_forms_created_by",
                table: "permission_forms",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "IX_peer_groups_approved_by",
                table: "peer_groups",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_mentor_assignments_approved_by",
                table: "mentor_assignments",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_contact_messages_handled_by",
                table: "contact_messages",
                column: "handled_by");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_documents_verified_by",
                table: "compliance_documents",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "IX_check_ins_closed_by",
                table: "check_ins",
                column: "closed_by");

            migrationBuilder.CreateIndex(
                name: "IX_check_ins_escalated_to",
                table: "check_ins",
                column: "escalated_to");

            migrationBuilder.CreateIndex(
                name: "IX_buddy_requests_approved_by",
                table: "buddy_requests",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_user_id",
                table: "audit_log",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_recorded_by",
                table: "attendance",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "IX_assessment_results_captured_by",
                table: "assessment_results",
                column: "captured_by");

            migrationBuilder.CreateIndex(
                name: "IX_announcements_published_by",
                table: "announcements",
                column: "published_by");

            migrationBuilder.AddForeignKey(
                name: "FK_announcements_users_published_by",
                table: "announcements",
                column: "published_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_assessment_results_users_captured_by",
                table: "assessment_results",
                column: "captured_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_attendance_users_recorded_by",
                table: "attendance",
                column: "recorded_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_users_user_id",
                table: "audit_log",
                column: "user_id",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_buddy_requests_users_approved_by",
                table: "buddy_requests",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_check_ins_users_closed_by",
                table: "check_ins",
                column: "closed_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_check_ins_users_escalated_to",
                table: "check_ins",
                column: "escalated_to",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_compliance_documents_users_verified_by",
                table: "compliance_documents",
                column: "verified_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_consents_permission_forms_form_id",
                table: "consents",
                column: "form_id",
                principalTable: "permission_forms",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_contact_messages_users_handled_by",
                table: "contact_messages",
                column: "handled_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_mentor_assignments_users_approved_by",
                table: "mentor_assignments",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_peer_groups_users_approved_by",
                table: "peer_groups",
                column: "approved_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_permission_forms_users_created_by",
                table: "permission_forms",
                column: "created_by",
                principalTable: "users",
                principalColumn: "id");

            migrationBuilder.AddForeignKey(
                name: "FK_volunteer_applications_users_reviewed_by",
                table: "volunteer_applications",
                column: "reviewed_by",
                principalTable: "users",
                principalColumn: "id");

            // ---- immutability, enforced by the database -----------------------

            migrationBuilder.Sql(
                "CREATE TRIGGER [trg_audit_log_immutable] ON [audit_log] INSTEAD OF UPDATE, DELETE AS "
                + "BEGIN SET NOCOUNT ON; THROW 51001, 'audit_log is immutable: entries can be added but never changed or removed.', 1; END");

            migrationBuilder.Sql(
                "CREATE TRIGGER [trg_consents_append_only] ON [consents] INSTEAD OF UPDATE, DELETE AS "
                + "BEGIN SET NOCOUNT ON; THROW 51002, 'consents is append-only: record a withdrawal as a new decision rather than changing or removing one.', 1; END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [trg_consents_append_only];");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS [trg_audit_log_immutable];");

            migrationBuilder.DropForeignKey(
                name: "FK_announcements_users_published_by",
                table: "announcements");

            migrationBuilder.DropForeignKey(
                name: "FK_assessment_results_users_captured_by",
                table: "assessment_results");

            migrationBuilder.DropForeignKey(
                name: "FK_attendance_users_recorded_by",
                table: "attendance");

            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_users_user_id",
                table: "audit_log");

            migrationBuilder.DropForeignKey(
                name: "FK_buddy_requests_users_approved_by",
                table: "buddy_requests");

            migrationBuilder.DropForeignKey(
                name: "FK_check_ins_users_closed_by",
                table: "check_ins");

            migrationBuilder.DropForeignKey(
                name: "FK_check_ins_users_escalated_to",
                table: "check_ins");

            migrationBuilder.DropForeignKey(
                name: "FK_compliance_documents_users_verified_by",
                table: "compliance_documents");

            migrationBuilder.DropForeignKey(
                name: "FK_consents_permission_forms_form_id",
                table: "consents");

            migrationBuilder.DropForeignKey(
                name: "FK_contact_messages_users_handled_by",
                table: "contact_messages");

            migrationBuilder.DropForeignKey(
                name: "FK_mentor_assignments_users_approved_by",
                table: "mentor_assignments");

            migrationBuilder.DropForeignKey(
                name: "FK_peer_groups_users_approved_by",
                table: "peer_groups");

            migrationBuilder.DropForeignKey(
                name: "FK_permission_forms_users_created_by",
                table: "permission_forms");

            migrationBuilder.DropForeignKey(
                name: "FK_volunteer_applications_users_reviewed_by",
                table: "volunteer_applications");

            migrationBuilder.DropIndex(
                name: "IX_volunteer_applications_reviewed_by",
                table: "volunteer_applications");

            migrationBuilder.DropIndex(
                name: "IX_permission_forms_created_by",
                table: "permission_forms");

            migrationBuilder.DropIndex(
                name: "IX_peer_groups_approved_by",
                table: "peer_groups");

            migrationBuilder.DropIndex(
                name: "IX_mentor_assignments_approved_by",
                table: "mentor_assignments");

            migrationBuilder.DropIndex(
                name: "IX_contact_messages_handled_by",
                table: "contact_messages");

            migrationBuilder.DropIndex(
                name: "IX_compliance_documents_verified_by",
                table: "compliance_documents");

            migrationBuilder.DropIndex(
                name: "IX_check_ins_closed_by",
                table: "check_ins");

            migrationBuilder.DropIndex(
                name: "IX_check_ins_escalated_to",
                table: "check_ins");

            migrationBuilder.DropIndex(
                name: "IX_buddy_requests_approved_by",
                table: "buddy_requests");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_user_id",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_attendance_recorded_by",
                table: "attendance");

            migrationBuilder.DropIndex(
                name: "IX_assessment_results_captured_by",
                table: "assessment_results");

            migrationBuilder.DropIndex(
                name: "IX_announcements_published_by",
                table: "announcements");

            migrationBuilder.AddForeignKey(
                name: "FK_consents_permission_forms_form_id",
                table: "consents",
                column: "form_id",
                principalTable: "permission_forms",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
