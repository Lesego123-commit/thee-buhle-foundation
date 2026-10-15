// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.repository

import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.TheeBuhleApi
import za.co.theebuhlefoundation.data.api.dto.*
import za.co.theebuhlefoundation.data.auth.SessionStore
import java.io.IOException
import java.net.SocketTimeoutException

/**
 * Turns whatever the transport throws into an [ApiError], so a view model
 * handles exactly one failure type and never sees an IOException.
 *
 * [ApiError] passes through untouched — the [ErrorInterceptor] has already
 * mapped the server's own words, and replacing them with something generic
 * here would throw away the useful half of the message.
 */
internal suspend fun <T> call(block: suspend () -> T): T =
    try {
        block()
    } catch (error: ApiError) {
        throw error
    } catch (_: SocketTimeoutException) {
        throw ApiError.timeout()
    } catch (_: IOException) {
        throw ApiError.offline()
    }

/**
 * Which learner a screen is about.
 *
 * A learner's own account carries the id. **A parent's does not** — a parent is
 * looking at one of their children — so it is resolved from the dashboard's
 * current selection instead.
 *
 * Resolved in one place because getting it wrong is invisible: asking the API
 * for "my subjects" as a parent is a valid request with an empty answer, so the
 * Learn screen showed "No subjects yet" to a parent whose child has four.
 */
class LearnerResolver(
    private val session: SessionStore,
    private val dashboard: suspend () -> ParentDashboard,
) {
    private var resolved: Resolved? = null

    /** The learner this account is currently about, with their grade. */
    suspend fun current(): Resolved? {
        resolved?.let { return it }

        // Both come from the dashboard, including for a learner's own account:
        // the account carries the id but not the grade, and every learning
        // endpoint needs the grade.
        resolved = runCatching {
            dashboard().selected?.let { Resolved(it.learnerId, it.grade) }
        }.getOrNull()

        return resolved
    }

    suspend fun id(): String? = current()?.learnerId

    suspend fun grade(): Int? = current()?.grade

    /** Called when a parent switches child, so the next screen asks about the new one. */
    fun forget() {
        resolved = null
    }

    data class Resolved(val learnerId: String, val grade: Int)
}

class AuthRepository(
    private val api: TheeBuhleApi,
    private val session: SessionStore,
) {
    suspend fun signIn(email: String, password: String): CurrentUser = call {
        val response = api.signIn(
            SignInRequest(
                email = email.trim(),
                password = password,
                device = "Android",
                returnRefreshTokenInBody = true,
            )
        )

        session.save(response.accessToken, response.refreshToken, response.user)

        response.user
    }

    /**
     * Ends the session locally whatever the network does.
     *
     * Revoking server-side is best effort: somebody who pressed Sign out on a
     * shared laptop at a Saturday session is signed out of this device before
     * the request has left it.
     */
    suspend fun signOut() {
        val refresh = session.refreshToken

        session.clear()

        runCatching { api.signOut(SignOutRequest(refresh)) }
    }

    suspend fun refreshUser(): CurrentUser = call { api.me() }

    suspend fun changePassword(current: String, replacement: String) = call {
        api.changePassword(ChangePasswordRequest(current, replacement))
    }

    /**
     * Sets a password with a code from the foundation. Signs nobody in: the
     * person signs in afterwards, with the password they just chose.
     */
    suspend fun redeemCode(email: String, code: String, newPassword: String) = call {
        api.redeemCode(RedeemCodeRequest(email.trim(), code.trim(), newPassword))
    }

    val currentUser: CurrentUser? get() = session.user

    val isSignedIn: Boolean get() = session.isSignedIn
}

class LearningRepository(
    private val api: TheeBuhleApi,
    private val session: SessionStore,
) {
    val learner = LearnerResolver(session) { dashboard() }

    suspend fun dashboard(learnerId: String? = null): ParentDashboard =
        call { api.dashboard(learnerId) }

    suspend fun subjects(): List<SubjectCard> = call {
        val grade = learner.grade() ?: return@call emptyList()

        api.subjects(grade)
    }

    suspend fun resources(subjectId: String, type: String? = null): List<ResourceCard> = call {
        val grade = learner.grade() ?: return@call emptyList()

        api.resources(subjectId, grade, type)
    }

    suspend fun search(query: String): List<ResourceCard> =
        call { api.searchResources(query, learner.grade()) }

    /**
     * Only asked for on a learner's own account.
     *
     * The endpoint is learner-only and answers 403 to a parent — correctly, it
     * is about what *they* were watching. Not asking is better than asking and
     * swallowing the refusal, which would put a 403 in the API's own logs
     * every time a parent opened the Learn tab.
     */
    suspend fun continueWatching(): ResourceCard? {
        if (session.user?.isLearner != true) return null

        return call { api.continueWatching() }
    }

    suspend fun recordProgress(resourceId: String, percentComplete: Int) = call {
        api.recordProgress(resourceId, ProgressRequest(percentComplete))
    }

    suspend fun downloadLink(resourceId: String): DownloadLink =
        call { api.downloadLink(resourceId) }
}

class ProgressRepository(
    private val api: TheeBuhleApi,
    private val session: SessionStore,
) {
    suspend fun progress(learnerId: String): LearnerProgress = call { api.progress(learnerId) }

    suspend fun badges(learnerId: String): List<BadgeCatalogueEntry> = call { api.badges(learnerId) }

    suspend fun milestone(learnerId: String): Milestone = call { api.milestone(learnerId) }

    suspend fun marks(learnerId: String): List<SubjectMarkTrend> = call { api.marks(learnerId) }

    suspend fun outstandingForms(learnerId: String): List<OutstandingForm> =
        call { api.outstandingForms(learnerId) }

    /** Records a guardian's answer. Only a Parent account may; the API refuses anyone else. */
    suspend fun recordConsent(formId: String, learnerId: String, granted: Boolean): ConsentResponse = call {
        api.recordConsent(formId, ConsentRequest(learnerId, if (granted) "Granted" else "Declined"))
    }
}

class PeerRepository(
    private val api: TheeBuhleApi,
    private val learner: LearnerResolver,
) {
    suspend fun suggestions(): List<BuddySuggestion> =
        call { api.buddySuggestions(learner.id()) }

    suspend fun groups(): List<StudyGroupCard> =
        call { api.studyGroups(learner.id()) }

    /** A request, never an approval. Only a mentor or staff member can approve. */
    suspend fun requestBuddy(recipientId: String) = call { api.requestBuddy(recipientId) }

    suspend fun joinGroup(groupId: String) = call { api.joinGroup(groupId) }
}

class MessagesRepository(private val api: TheeBuhleApi) {

    suspend fun messages(): List<InboxMessage> = call { api.messages() }

    suspend fun markMessageRead(announcementId: String) = call { api.markMessageRead(announcementId) }

    suspend fun notifications(): List<NotificationItem> = call { api.notifications() }

    suspend fun unreadCount(): Int = call { api.unreadCount().unread }

    suspend fun markNotificationRead(id: String) = call { api.markNotificationRead(id) }

    suspend fun markAllRead() = call { api.markAllRead() }
}

class MentorRepository(private val api: TheeBuhleApi) {

    suspend fun dashboard(): MentorDashboard = call { api.mentorDashboard() }

    suspend fun logCheckIn(request: LogCheckInRequest): CheckInResponse =
        call { api.logCheckIn(request) }
}
