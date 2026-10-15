// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
//    - JetBrains (2024) kotlinx.serialization documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api.dto

import kotlinx.serialization.Serializable

// -----------------------------------------------------------------------------
//  Dashboard
// -----------------------------------------------------------------------------

@Serializable
data class ParentDashboard(
    val displayName: String,
    val children: List<LearnerCard> = emptyList(),
    val selected: LearnerPanel? = null,
    val unreadNotifications: Int = 0,
    val unreadMessages: Int = 0,
    val inbox: List<InboxMessage> = emptyList(),
    val asOf: String,
)

@Serializable
data class LearnerCard(
    val learnerId: String,
    val referenceCode: String,
    val firstName: String,
    val surname: String,
    val grade: Int,
    val school: String? = null,
    val suburb: String? = null,
    val attendanceRate: Int,
    val isPrimary: Boolean = false,
) {
    val fullName: String get() = "$firstName $surname"
}

@Serializable
data class LearnerPanel(
    val learnerId: String,
    val firstName: String,
    val surname: String,
    val grade: Int,
    val nextSession: SessionCard? = null,
    val attendance: AttendanceSummary,
    val attendanceRate: Int,
    val headlineSubject: SubjectMarkTrend? = null,
    val badgeCount: Int,
    val recentAttendance: List<AttendanceEntry> = emptyList(),
    val outstandingForms: List<OutstandingForm> = emptyList(),
)

@Serializable
data class SessionCard(
    val sessionId: String,
    val startsAt: String,
    val endsAt: String,
    val subjectFocus: String? = null,
    val room: String? = null,
    val programmeName: String,
    val venueName: String,
    val venueAddress: String,
)

@Serializable
data class AttendanceSummary(
    val sessionsRecorded: Int,
    val sessionsAttended: Int,
    val sessionsLate: Int,
    val sessionsAbsent: Int,
    val attendanceRate: Int,
)

@Serializable
data class AttendanceEntry(
    val attendanceId: String,
    /** "Present", "Late", "Absent" or "Excused". */
    val status: String,
    val arrivedAt: String? = null,
    val note: String? = null,
    val sessionId: String,
    val startsAt: String,
    val subjectFocus: String? = null,
    val room: String? = null,
    val venueName: String? = null,
    val programmeName: String,
)

// -----------------------------------------------------------------------------
//  Progress
// -----------------------------------------------------------------------------

@Serializable
data class LearnerProgress(
    val learnerId: String,
    val referenceCode: String,
    val firstName: String,
    val surname: String,
    val grade: Int,
    val school: String? = null,
    val attendance: AttendanceSummary,
    val streak: Int,
    val recentAttendance: List<AttendanceEntry> = emptyList(),
    val marks: List<SubjectMarkTrend> = emptyList(),
    val mentor: AssignmentView? = null,
    val checkIns: List<CheckInView> = emptyList(),
    val badges: List<AwardedBadge> = emptyList(),
    val milestone: Milestone,
    val outstandingForms: List<OutstandingForm> = emptyList(),
    val asOf: String,
)

@Serializable
data class SubjectMarkTrend(
    val subjectId: String,
    val subjectName: String,
    val firstMark: Double,
    val latestMark: Double,
    val entries: Int,
    /**
     * Computed by the API as latest minus first, and **negative when marks have
     * fallen**. The app shows the sign rather than hiding it — a subject going
     * backwards is the one a parent most needs to see.
     */
    val improvement: Double,
)

@Serializable
data class AssignmentView(
    val assignmentId: String,
    val mentorId: String,
    val learnerId: String,
    val schoolYear: Int,
    val mentorName: String,
    val occupation: String? = null,
)

@Serializable
data class CheckInView(
    val checkInId: String,
    val occurredOn: String,
    val mode: String,
    val wellbeing: String,
    val notes: String? = null,
    val supportAreas: List<String> = emptyList(),
    val concernFlagged: Boolean = false,
    val escalatedAt: String? = null,
    val mentorName: String,
    val occupation: String? = null,
)
