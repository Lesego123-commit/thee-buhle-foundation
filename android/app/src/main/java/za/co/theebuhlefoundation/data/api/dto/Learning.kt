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
//  Learning
// -----------------------------------------------------------------------------

@Serializable
data class SubjectCard(
    val subjectId: String,
    val name: String,
    val grade: Int,
    val icon: String? = null,
    val accent: String? = null,
    val resourceCount: Int,
    val completedCount: Int,
)

@Serializable
data class ResourceCard(
    val resourceId: String,
    val subjectId: String,
    val subjectName: String,
    val title: String,
    val topic: String? = null,
    /** "Video", "Worksheet", "PastPaper" or "ReadingList", as the API names them. */
    val type: String,
    val grade: Int,
    val sizeBytes: Long? = null,
    val durationMinutes: Int? = null,
    val percentComplete: Int = 0,
    val publishedAt: String? = null,
)

@Serializable
data class ProgressRequest(val percentComplete: Int)

@Serializable
data class DownloadLink(val url: String, val expiresAt: String)

// -----------------------------------------------------------------------------
//  Badges
// -----------------------------------------------------------------------------

@Serializable
data class AwardedBadge(
    val badgeId: String,
    val code: String,
    val name: String,
    val description: String,
    val icon: String? = null,
    val points: Int,
    val awardedAt: String,
)

/**
 * A badge in the catalogue, earned or not.
 *
 * [awardedAt] is null until it is earned, which is why this is a separate
 * shape from [AwardedBadge] — the progress endpoint sends only what a learner
 * has, this one sends everything there is.
 */
@Serializable
data class BadgeCatalogueEntry(
    val badgeId: String,
    val code: String,
    val name: String,
    val description: String,
    val icon: String? = null,
    val criteria: String,
    val threshold: Int,
    val points: Int,
    val earned: Boolean = false,
    val awardedAt: String? = null,
)

@Serializable
data class Milestone(
    val points: Int,
    val milestone: Int,
    val milestoneName: String,
    val nextTarget: Int,
    val progressPercent: Int,
    val nextBadge: NextBadge? = null,
)

@Serializable
data class NextBadge(
    val code: String,
    val name: String,
    val description: String,
    val icon: String? = null,
    val achieved: Int,
    val threshold: Int,
    val remaining: Int,
)

// -----------------------------------------------------------------------------
//  Peers
// -----------------------------------------------------------------------------

@Serializable
data class BuddySuggestion(
    val learnerId: String,
    val referenceCode: String,
    /**
     * A first name only. The API never sends a surname here — a learner is
     * shown to another learner by enough to recognise somebody from a Saturday
     * session and not enough to identify a stranger's child.
     */
    val firstName: String,
    val grade: Int,
    val sharedSubjects: List<String> = emptyList(),
)

@Serializable
data class StudyGroupCard(
    val groupId: String,
    val name: String,
    val subjectName: String? = null,
    val meetsAt: String? = null,
    val location: String? = null,
    val memberCount: Int,
    /** "Requested", "Active", "Declined" or null when not a member. */
    val myStatus: String? = null,
)

// -----------------------------------------------------------------------------
//  Messages, notifications and consent
// -----------------------------------------------------------------------------

@Serializable
data class InboxMessage(
    val announcementId: String,
    val title: String,
    val bodyHtml: String,
    val category: String,
    val actionRequired: Boolean = false,
    val dueDate: String? = null,
    val publishedAt: String,
    val readAt: String? = null,
    val senderName: String,
)

@Serializable
data class NotificationItem(
    val id: String,
    val category: String,
    val icon: String? = null,
    val title: String,
    val body: String,
    val link: String? = null,
    val readAt: String? = null,
    val createdAt: String,
)

@Serializable
data class UnreadCount(val unread: Int)

@Serializable
data class OutstandingForm(
    val formId: String,
    val title: String,
    val body: String,
    val dueDate: String,
    val eventTitle: String? = null,
    val eventStartsAt: String? = null,
    val eventLocation: String? = null,
    val programmeName: String? = null,
    /**
     * Null when the guardian has never answered, or "Withdrawn" when they
     * granted and then took it back: a withdrawn form is waiting again.
     */
    val lastDecision: String? = null,
)

/**
 * A guardian's answer to a form. The decision is the API's ConsentDecision
 * name, "Granted" or "Declined". There is no signature field: the server
 * derives the signature reference from the authenticated guardian itself,
 * because a client-supplied one would prove nothing.
 */
@Serializable
data class ConsentRequest(val learnerId: String, val decision: String)

@Serializable
data class ConsentResponse(
    val consentId: String,
    val decision: String,
    val signatureRef: String,
    val signedAt: String,
    val message: String,
)

// -----------------------------------------------------------------------------
//  Mentor
// -----------------------------------------------------------------------------

@Serializable
data class MentorDashboard(
    val displayName: String,
    val mentees: List<MenteeCard> = emptyList(),
    val asOf: String,
)

@Serializable
data class MenteeCard(
    val assignmentId: String,
    val learnerId: String,
    val referenceCode: String,
    val firstName: String,
    val surname: String,
    val grade: Int,
    val suburb: String? = null,
    val checkInCount: Int,
    val lastCheckInOn: String? = null,
    val daysSince: Int? = null,
    val isOverdue: Boolean = false,
) {
    val fullName: String get() = "$firstName $surname"
}

@Serializable
data class LogCheckInRequest(
    val assignmentId: String,
    /** ISO date, yyyy-MM-dd. */
    val occurredOn: String,
    /** "InPerson", "Phone", "Message" or "Group". */
    val mode: String,
    /** "Thriving", "Steady", "Unsettled" or "Concerned". */
    val wellbeing: String,
    val notes: String? = null,
    val supportAreas: List<String> = emptyList(),
    val concernFlagged: Boolean = false,
)

@Serializable
data class CheckInResponse(
    val checkInId: String,
    val escalated: Boolean,
    val message: String,
)
