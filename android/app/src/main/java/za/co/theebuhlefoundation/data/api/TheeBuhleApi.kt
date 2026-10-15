// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api

import retrofit2.http.Body
import retrofit2.http.GET
import retrofit2.http.POST
import retrofit2.http.PUT
import retrofit2.http.Path
import retrofit2.http.Query
import za.co.theebuhlefoundation.data.api.dto.*

/**
 * Every call this app makes to the API, in one interface.
 *
 * The paths are relative to the base URL, which ends in `/api/` — so
 * `auth/sign-in` here is `/api/auth/sign-in` on the server. They were written
 * against the running API and each one is exercised by a repository test, for
 * a specific reason: a previous client for this system was written against a
 * different API's routes, compiled cleanly, passed its own tests against a
 * mocked transport, and could not reach a single endpoint.
 */
interface TheeBuhleApi {

    // ----------------------------------------------------------------- auth

    @POST("auth/sign-in")
    suspend fun signIn(@Body request: SignInRequest): SignInResponse

    @POST("auth/refresh")
    suspend fun refresh(@Body request: RefreshRequest): SignInResponse

    @POST("auth/sign-out")
    suspend fun signOut(@Body request: SignOutRequest)

    @POST("auth/change-password")
    suspend fun changePassword(@Body request: ChangePasswordRequest)

    @POST("auth/redeem-code")
    suspend fun redeemCode(@Body request: RedeemCodeRequest)

    @GET("auth/me")
    suspend fun me(): CurrentUser

    // ------------------------------------------------------------ dashboard

    @GET("dashboard")
    suspend fun dashboard(@Query("learnerId") learnerId: String? = null): ParentDashboard

    @GET("learners/{learnerId}/progress")
    suspend fun progress(@Path("learnerId") learnerId: String): LearnerProgress

    // ------------------------------------------------------------- learning

    /**
     * Subjects for a grade.
     *
     * `grade` is **required** — the API has no default and answers an empty
     * list without it, which reads on screen as "this learner has no
     * subjects". There is no learnerId parameter either: the API takes the
     * learner from the token, so a parent gets the catalogue for the grade
     * without their child's per-resource progress on it.
     */
    @GET("learning/subjects")
    suspend fun subjects(@Query("grade") grade: Int): List<SubjectCard>

    @GET("learning/subjects/{subjectId}/resources")
    suspend fun resources(
        @Path("subjectId") subjectId: String,
        @Query("grade") grade: Int,
        @Query("type") type: String? = null,
    ): List<ResourceCard>

    @GET("learning/search")
    suspend fun searchResources(
        @Query("query") query: String,
        @Query("grade") grade: Int? = null,
        @Query("limit") limit: Int = 20,
    ): List<ResourceCard>

    @GET("learning/continue")
    suspend fun continueWatching(): ResourceCard?

    @POST("learning/resources/{resourceId}/progress")
    suspend fun recordProgress(
        @Path("resourceId") resourceId: String,
        @Body request: ProgressRequest,
    )

    @POST("learning/resources/{resourceId}/download")
    suspend fun downloadLink(@Path("resourceId") resourceId: String): DownloadLink

    /**
     * The badge catalogue for a learner: every badge, earned or not.
     *
     * Not a list of awards — `awardedAt` is null for a badge still to be
     * earned, which is what lets the screen show what is left to aim for.
     */
    @GET("learning/learners/{learnerId}/badges")
    suspend fun badges(@Path("learnerId") learnerId: String): List<BadgeCatalogueEntry>

    @GET("learning/learners/{learnerId}/milestone")
    suspend fun milestone(@Path("learnerId") learnerId: String): Milestone

    @GET("learning/learners/{learnerId}/marks")
    suspend fun marks(@Path("learnerId") learnerId: String): List<SubjectMarkTrend>

    // ---------------------------------------------------------------- peers

    @GET("peers/suggestions")
    suspend fun buddySuggestions(@Query("learnerId") learnerId: String? = null): List<BuddySuggestion>

    @GET("peers/groups")
    suspend fun studyGroups(@Query("learnerId") learnerId: String? = null): List<StudyGroupCard>

    /** A learner may only *request*. Approval is a mentor or staff action. */
    @POST("peers/requests/{recipientId}")
    suspend fun requestBuddy(@Path("recipientId") recipientId: String)

    @POST("peers/groups/{groupId}/join")
    suspend fun joinGroup(@Path("groupId") groupId: String)

    // ------------------------------------------- messages and notifications

    @GET("messages")
    suspend fun messages(@Query("limit") limit: Int = 30): List<InboxMessage>

    @POST("messages/{announcementId}/read")
    suspend fun markMessageRead(@Path("announcementId") announcementId: String)

    @GET("notifications")
    suspend fun notifications(@Query("limit") limit: Int = 30): List<NotificationItem>

    @GET("notifications/unread-count")
    suspend fun unreadCount(): UnreadCount

    @POST("notifications/{notificationId}/read")
    suspend fun markNotificationRead(@Path("notificationId") notificationId: String)

    @POST("notifications/read-all")
    suspend fun markAllRead()

    // -------------------------------------------------------------- consent

    @GET("learners/{learnerId}/forms")
    suspend fun outstandingForms(@Path("learnerId") learnerId: String): List<OutstandingForm>

    @POST("forms/{formId}/consent")
    suspend fun recordConsent(@Path("formId") formId: String, @Body request: ConsentRequest): ConsentResponse

    // --------------------------------------------------------------- mentor

    @GET("mentor/dashboard")
    suspend fun mentorDashboard(): MentorDashboard

    @POST("mentor/check-ins")
    suspend fun logCheckIn(@Body request: LogCheckInRequest): CheckInResponse
}
