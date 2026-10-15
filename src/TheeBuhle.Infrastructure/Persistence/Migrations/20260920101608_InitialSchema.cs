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
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body_html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    audience = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    action_required = table.Column<bool>(type: "bit", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    published_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_announcements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    entity_name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    entity_id = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    before_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    after_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    user_agent = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "badges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    code = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    icon = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    criteria = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    threshold = table.Column<int>(type: "int", nullable: false),
                    points = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badges", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contact_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    topic = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    organisation = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    email = table.Column<string>(type: "nvarchar(190)", maxLength: 190, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    consent_popia = table.Column<bool>(type: "bit", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    handled_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "impact_metrics",
                columns: table => new
                {
                    metric_key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    school_year = table.Column<int>(type: "int", nullable: false),
                    label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    value_text = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    footnote = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    computed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_impact_metrics", x => new { x.metric_key, x.school_year });
                });

            migrationBuilder.CreateTable(
                name: "news_posts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    excerpt = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    body_html = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    author_name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    read_minutes = table.Column<int>(type: "int", nullable: false),
                    is_featured = table.Column<bool>(type: "bit", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_news_posts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "newsletter_subscribers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(190)", maxLength: 190, nullable: false, collation: "Latin1_General_CI_AS"),
                    confirmed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    unsubscribed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_newsletter_subscribers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "programmes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    slug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    number = table.Column<string>(type: "nvarchar(4)", maxLength: 4, nullable: false),
                    name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    summary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    expectations = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    how_to_join = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    cta_label = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    audience = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    capacity_note = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    min_grade = table.Column<int>(type: "int", nullable: false),
                    max_grade = table.Column<int>(type: "int", nullable: false),
                    is_active = table.Column<bool>(type: "bit", nullable: false),
                    sort_order = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_programmes", x => x.id);
                    table.CheckConstraint("ck_programme_grades", "[max_grade] >= [min_grade]");
                });

            migrationBuilder.CreateTable(
                name: "subjects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    grade = table.Column<int>(type: "int", nullable: false),
                    icon = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    accent = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subjects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    email = table.Column<string>(type: "nvarchar(190)", maxLength: 190, nullable: false, collation: "Latin1_General_CI_AS"),
                    password_hash = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    mfa_enabled = table.Column<bool>(type: "bit", nullable: false),
                    failed_attempts = table.Column<int>(type: "int", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "venues",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    address = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_venues", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    topic = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    grade = table.Column<int>(type: "int", nullable: false),
                    storage_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    duration_minutes = table.Column<int>(type: "int", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resources", x => x.id);
                    table.ForeignKey(
                        name: "FK_resources_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "device_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    platform = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    app_version = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    last_seen_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_device_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_device_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "donors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    display_name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    email = table.Column<string>(type: "nvarchar(190)", maxLength: 190, nullable: false, collation: "Latin1_General_CI_AS"),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    is_organisation = table.Column<bool>(type: "bit", nullable: false),
                    tax_number = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donors", x => x.id);
                    table.ForeignKey(
                        name: "FK_donors_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "learners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reference_code = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: false),
                    grade = table.Column<int>(type: "int", nullable: false),
                    school = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    suburb = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ambition = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    photo_consent = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learners", x => x.id);
                    table.CheckConstraint("ck_learners_grade", "[grade] BETWEEN 1 AND 12");
                    table.ForeignKey(
                        name: "FK_learners_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "mentors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    occupation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    clearance_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    clearance_expiry = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentors", x => x.id);
                    table.CheckConstraint("ck_mentor_clearance", "[clearance_status] <> 'Cleared' OR [clearance_expiry] IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_mentors_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "message_recipients",
                columns: table => new
                {
                    announcement_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_message_recipients", x => new { x.announcement_id, x.user_id });
                    table.ForeignKey(
                        name: "FK_message_recipients_announcements_announcement_id",
                        column: x => x.announcement_id,
                        principalTable: "announcements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_message_recipients_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    enabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_preferences", x => new { x.user_id, x.category });
                    table.ForeignKey(
                        name: "FK_notification_preferences_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    icon = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    link = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    channel = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    delivery_status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parent_guardians",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    alt_phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    relationship = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    contact_confirmed_at = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_parent_guardians", x => x.id);
                    table.ForeignKey(
                        name: "FK_parent_guardians_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    token_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    device = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "staff",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    first_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    job_title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    is_safeguarding_lead = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_staff", x => x.id);
                    table.ForeignKey(
                        name: "FK_staff_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "volunteer_applications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    user_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    first_name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    surname = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    email = table.Column<string>(type: "nvarchar(190)", maxLength: 190, nullable: false),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    suburb = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    id_number_last4 = table.Column<string>(type: "nchar(4)", fixedLength: true, maxLength: 4, nullable: true),
                    skills = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    availability = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    experience = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    consent_popia = table.Column<bool>(type: "bit", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volunteer_applications", x => x.id);
                    table.ForeignKey(
                        name: "FK_volunteer_applications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    venue_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    location = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    starts_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    fee = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    fee_note = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    is_published = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.id);
                    table.CheckConstraint("ck_event_window", "[ends_at] IS NULL OR [ends_at] >= [starts_at]");
                    table.ForeignKey(
                        name: "FK_events_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "peer_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    venue_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    meets_at = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    location = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: true),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_peer_groups", x => x.id);
                    table.ForeignKey(
                        name: "FK_peer_groups_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_peer_groups_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    programme_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    venue_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    subject_focus = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    room = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    capacity = table.Column<int>(type: "int", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.CheckConstraint("ck_session_window", "[ends_at] > [starts_at]");
                    table.ForeignKey(
                        name: "FK_sessions_programmes_programme_id",
                        column: x => x.programme_id,
                        principalTable: "programmes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sessions_venues_venue_id",
                        column: x => x.venue_id,
                        principalTable: "venues",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "donations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    donor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "nvarchar(3)", maxLength: 3, nullable: false),
                    frequency = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    designation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    gateway = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    gateway_reference = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    wants_section18_a = table.Column<bool>(type: "bit", nullable: false),
                    section18_a_issued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_donations", x => x.id);
                    table.CheckConstraint("ck_donation_amount", "[amount] > 0");
                    table.ForeignKey(
                        name: "FK_donations_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "assessment_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    subject_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    term = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    mark = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    captured_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assessment_results", x => x.id);
                    table.CheckConstraint("ck_ar_mark", "[mark] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_assessment_results_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_assessment_results_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "buddy_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    requester_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    recipient_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_buddy_requests", x => x.id);
                    table.CheckConstraint("ck_buddy_not_self", "[requester_id] <> [recipient_id]");
                    table.ForeignKey(
                        name: "FK_buddy_requests_learners_recipient_id",
                        column: x => x.recipient_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_buddy_requests_learners_requester_id",
                        column: x => x.requester_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "enrolments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    programme_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    term = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    enrolled_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrolments", x => x.id);
                    table.ForeignKey(
                        name: "FK_enrolments_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_enrolments_programmes_programme_id",
                        column: x => x.programme_id,
                        principalTable: "programmes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learner_badges",
                columns: table => new
                {
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    badge_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    awarded_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learner_badges", x => new { x.learner_id, x.badge_id });
                    table.ForeignKey(
                        name: "FK_learner_badges_badges_badge_id",
                        column: x => x.badge_id,
                        principalTable: "badges",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learner_badges_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learner_summaries",
                columns: table => new
                {
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sessions_recorded = table.Column<int>(type: "int", nullable: false),
                    sessions_attended = table.Column<int>(type: "int", nullable: false),
                    sessions_late = table.Column<int>(type: "int", nullable: false),
                    attendance_rate = table.Column<int>(type: "int", nullable: false),
                    refreshed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learner_summaries", x => x.learner_id);
                    table.ForeignKey(
                        name: "FK_learner_summaries_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "resource_progress",
                columns: table => new
                {
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    resource_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    percent_complete = table.Column<int>(type: "int", nullable: false),
                    last_opened_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_resource_progress", x => new { x.learner_id, x.resource_id });
                    table.CheckConstraint("ck_rp_percent", "[percent_complete] BETWEEN 0 AND 100");
                    table.ForeignKey(
                        name: "FK_resource_progress_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_resource_progress_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sponsorships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    donor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    annual_amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sponsorships", x => x.id);
                    table.CheckConstraint("ck_sp_window", "[end_date] IS NULL OR [end_date] > [start_date]");
                    table.ForeignKey(
                        name: "FK_sponsorships_donors_donor_id",
                        column: x => x.donor_id,
                        principalTable: "donors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_sponsorships_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "mentor_assignments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    mentor_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    school_year = table.Column<int>(type: "int", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    approved_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mentor_assignments", x => x.id);
                    table.ForeignKey(
                        name: "FK_mentor_assignments_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_mentor_assignments_mentors_mentor_id",
                        column: x => x.mentor_id,
                        principalTable: "mentors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guardian_links",
                columns: table => new
                {
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    is_primary = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_guardian_links", x => new { x.parent_id, x.learner_id });
                    table.ForeignKey(
                        name: "FK_guardian_links_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_guardian_links_parent_guardians_parent_id",
                        column: x => x.parent_id,
                        principalTable: "parent_guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "compliance_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    application_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    doc_type = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    storage_path = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    verified_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    expires_at = table.Column<DateOnly>(type: "date", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_compliance_documents", x => x.id);
                    table.ForeignKey(
                        name: "FK_compliance_documents_volunteer_applications_application_id",
                        column: x => x.application_id,
                        principalTable: "volunteer_applications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event_registrations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    attendee_name = table.Column<string>(type: "nvarchar(140)", maxLength: 140, nullable: false),
                    email = table.Column<string>(type: "nvarchar(190)", maxLength: 190, nullable: false, collation: "Latin1_General_CI_AS"),
                    phone = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    party_size = table.Column<int>(type: "int", nullable: false),
                    paid = table.Column<bool>(type: "bit", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_registrations", x => x.id);
                    table.CheckConstraint("ck_er_party", "[party_size] BETWEEN 1 AND 20");
                    table.ForeignKey(
                        name: "FK_event_registrations_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_event_registrations_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "permission_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    event_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    programme_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission_forms", x => x.id);
                    table.CheckConstraint("ck_pf_target", "([event_id] IS NOT NULL AND [programme_id] IS NULL) OR ([event_id] IS NULL AND [programme_id] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_permission_forms_events_event_id",
                        column: x => x.event_id,
                        principalTable: "events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_permission_forms_programmes_programme_id",
                        column: x => x.programme_id,
                        principalTable: "programmes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "group_memberships",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_group_memberships", x => new { x.group_id, x.learner_id });
                    table.ForeignKey(
                        name: "FK_group_memberships_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_group_memberships_peer_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "peer_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    session_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    arrived_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    note = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance", x => x.id);
                    table.ForeignKey(
                        name: "FK_attendance_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_attendance_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "check_ins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    assignment_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    mode = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    wellbeing = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    support_areas = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    concern_flagged = table.Column<bool>(type: "bit", nullable: false),
                    escalated_to = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    escalated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    closed_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    outcome = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_check_ins", x => x.id);
                    table.ForeignKey(
                        name: "FK_check_ins_mentor_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalTable: "mentor_assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "consents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    sequence = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    form_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    learner_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    parent_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    decision = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    signed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    signature_ref = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ip_address = table.Column<string>(type: "nvarchar(45)", maxLength: 45, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    updated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consents", x => x.id);
                    table.ForeignKey(
                        name: "FK_consents_learners_learner_id",
                        column: x => x.learner_id,
                        principalTable: "learners",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consents_parent_guardians_parent_id",
                        column: x => x.parent_id,
                        principalTable: "parent_guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_consents_permission_forms_form_id",
                        column: x => x.form_id,
                        principalTable: "permission_forms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcement_audience",
                table: "announcements",
                columns: new[] { "audience", "published_at" });

            migrationBuilder.CreateIndex(
                name: "IX_assessment_results_subject_id",
                table: "assessment_results",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "uk_result_once",
                table: "assessment_results",
                columns: new[] { "learner_id", "subject_id", "term", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attendance_learner",
                table: "attendance",
                columns: new[] { "learner_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uk_attendance_once",
                table: "attendance",
                columns: new[] { "session_id", "learner_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_audit_entity",
                table: "audit_log",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_time",
                table: "audit_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "uk_badge_code",
                table: "badges",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_buddy_requests_recipient_id",
                table: "buddy_requests",
                column: "recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_buddy_status",
                table: "buddy_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "uk_buddy_pair",
                table: "buddy_requests",
                columns: new[] { "requester_id", "recipient_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_checkin_assignment",
                table: "check_ins",
                columns: new[] { "assignment_id", "occurred_on" });

            migrationBuilder.CreateIndex(
                name: "ix_checkin_concern_open",
                table: "check_ins",
                column: "occurred_on",
                filter: "[concern_flagged] = 1 AND [closed_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_compliance_documents_application_id",
                table: "compliance_documents",
                column: "application_id");

            migrationBuilder.CreateIndex(
                name: "ix_consent_current",
                table: "consents",
                columns: new[] { "form_id", "learner_id", "sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_consents_learner_id",
                table: "consents",
                column: "learner_id");

            migrationBuilder.CreateIndex(
                name: "IX_consents_parent_id",
                table: "consents",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_status",
                table: "contact_messages",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_device_tokens_user_id",
                table: "device_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uk_device_token",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_donation_s18a_pending",
                table: "donations",
                column: "section18_a_issued_at",
                filter: "[section18_a_issued_at] IS NULL AND [wants_section18_a] = 1");

            migrationBuilder.CreateIndex(
                name: "ix_donation_status",
                table: "donations",
                columns: new[] { "status", "received_at" });

            migrationBuilder.CreateIndex(
                name: "IX_donations_donor_id",
                table: "donations",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "uk_donation_gateway_ref",
                table: "donations",
                column: "gateway_reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_donors_user_id",
                table: "donors",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "uk_donor_email",
                table: "donors",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_enrolments_programme_id",
                table: "enrolments",
                column: "programme_id");

            migrationBuilder.CreateIndex(
                name: "uk_enrolment_unique",
                table: "enrolments",
                columns: new[] { "learner_id", "programme_id", "term" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_registrations_learner_id",
                table: "event_registrations",
                column: "learner_id");

            migrationBuilder.CreateIndex(
                name: "uk_event_reg_email",
                table: "event_registrations",
                columns: new[] { "event_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_published",
                table: "events",
                columns: new[] { "is_published", "starts_at" });

            migrationBuilder.CreateIndex(
                name: "IX_events_venue_id",
                table: "events",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "uk_event_slug",
                table: "events",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_group_memberships_learner_id",
                table: "group_memberships",
                column: "learner_id");

            migrationBuilder.CreateIndex(
                name: "ix_guardian_learner",
                table: "guardian_links",
                column: "learner_id");

            migrationBuilder.CreateIndex(
                name: "IX_learner_badges_badge_id",
                table: "learner_badges",
                column: "badge_id");

            migrationBuilder.CreateIndex(
                name: "ix_learners_grade",
                table: "learners",
                column: "grade");

            migrationBuilder.CreateIndex(
                name: "uk_learners_reference",
                table: "learners",
                column: "reference_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_learners_user",
                table: "learners",
                column: "user_id",
                unique: true,
                filter: "[user_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_mentor",
                table: "mentor_assignments",
                columns: new[] { "mentor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "uk_assignment_active",
                table: "mentor_assignments",
                columns: new[] { "learner_id", "school_year" },
                unique: true,
                filter: "[status] = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_mentor_clearance",
                table: "mentors",
                columns: new[] { "clearance_status", "clearance_expiry" });

            migrationBuilder.CreateIndex(
                name: "uk_mentor_user",
                table: "mentors",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_recipient_unread",
                table: "message_recipients",
                columns: new[] { "user_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_news_published",
                table: "news_posts",
                columns: new[] { "category", "published_at" });

            migrationBuilder.CreateIndex(
                name: "uk_news_slug",
                table: "news_posts",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_news_sub_email",
                table: "newsletter_subscribers",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_unread",
                table: "notifications",
                columns: new[] { "user_id", "created_at" },
                filter: "[read_at] IS NULL");

            migrationBuilder.CreateIndex(
                name: "uk_parent_user",
                table: "parent_guardians",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_peer_groups_subject_id",
                table: "peer_groups",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "IX_peer_groups_venue_id",
                table: "peer_groups",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_permission_forms_event_id",
                table: "permission_forms",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "IX_permission_forms_programme_id",
                table: "permission_forms",
                column: "programme_id");

            migrationBuilder.CreateIndex(
                name: "uk_programme_slug",
                table: "programmes",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_user",
                table: "refresh_tokens",
                columns: new[] { "user_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "uk_refresh_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_resource_progress_resource_id",
                table: "resource_progress",
                column: "resource_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_subject",
                table: "resources",
                columns: new[] { "subject_id", "grade" });

            migrationBuilder.CreateIndex(
                name: "ix_session_starts",
                table: "sessions",
                column: "starts_at");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_programme_id",
                table: "sessions",
                column: "programme_id");

            migrationBuilder.CreateIndex(
                name: "IX_sessions_venue_id",
                table: "sessions",
                column: "venue_id");

            migrationBuilder.CreateIndex(
                name: "IX_sponsorships_donor_id",
                table: "sponsorships",
                column: "donor_id");

            migrationBuilder.CreateIndex(
                name: "uk_sponsorship_active",
                table: "sponsorships",
                column: "learner_id",
                unique: true,
                filter: "[end_date] IS NULL");

            migrationBuilder.CreateIndex(
                name: "uk_staff_single_lead",
                table: "staff",
                column: "is_safeguarding_lead",
                unique: true,
                filter: "[is_safeguarding_lead] = 1");

            migrationBuilder.CreateIndex(
                name: "uk_staff_user",
                table: "staff",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uk_subject_name_grade",
                table: "subjects",
                columns: new[] { "name", "grade" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role",
                table: "users",
                columns: new[] { "role", "status" });

            migrationBuilder.CreateIndex(
                name: "uk_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_volunteer_applications_user_id",
                table: "volunteer_applications",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_volunteer_status",
                table: "volunteer_applications",
                columns: new[] { "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assessment_results");

            migrationBuilder.DropTable(
                name: "attendance");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "buddy_requests");

            migrationBuilder.DropTable(
                name: "check_ins");

            migrationBuilder.DropTable(
                name: "compliance_documents");

            migrationBuilder.DropTable(
                name: "consents");

            migrationBuilder.DropTable(
                name: "contact_messages");

            migrationBuilder.DropTable(
                name: "device_tokens");

            migrationBuilder.DropTable(
                name: "donations");

            migrationBuilder.DropTable(
                name: "enrolments");

            migrationBuilder.DropTable(
                name: "event_registrations");

            migrationBuilder.DropTable(
                name: "group_memberships");

            migrationBuilder.DropTable(
                name: "guardian_links");

            migrationBuilder.DropTable(
                name: "impact_metrics");

            migrationBuilder.DropTable(
                name: "learner_badges");

            migrationBuilder.DropTable(
                name: "learner_summaries");

            migrationBuilder.DropTable(
                name: "message_recipients");

            migrationBuilder.DropTable(
                name: "news_posts");

            migrationBuilder.DropTable(
                name: "newsletter_subscribers");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "resource_progress");

            migrationBuilder.DropTable(
                name: "sponsorships");

            migrationBuilder.DropTable(
                name: "staff");

            migrationBuilder.DropTable(
                name: "sessions");

            migrationBuilder.DropTable(
                name: "mentor_assignments");

            migrationBuilder.DropTable(
                name: "volunteer_applications");

            migrationBuilder.DropTable(
                name: "permission_forms");

            migrationBuilder.DropTable(
                name: "peer_groups");

            migrationBuilder.DropTable(
                name: "parent_guardians");

            migrationBuilder.DropTable(
                name: "badges");

            migrationBuilder.DropTable(
                name: "announcements");

            migrationBuilder.DropTable(
                name: "resources");

            migrationBuilder.DropTable(
                name: "donors");

            migrationBuilder.DropTable(
                name: "learners");

            migrationBuilder.DropTable(
                name: "mentors");

            migrationBuilder.DropTable(
                name: "events");

            migrationBuilder.DropTable(
                name: "programmes");

            migrationBuilder.DropTable(
                name: "subjects");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "venues");
        }
    }
}
