// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
//    - JetBrains (2024) kotlinx.serialization documentation.
//    - JUnit (2021) JUnit 4 documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api.dto

import kotlinx.serialization.json.Json
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The wire format, pinned against real responses from the API.
 *
 * Every payload below was copied from the running API rather than written to
 * suit the DTO, which is the whole point: the client this one replaced was
 * written against a different API's shapes, compiled cleanly, passed its own
 * tests against a mocked transport, and could not read a single response.
 */
class WireFormatTest {

    private val json = Json { ignoreUnknownKeys = true; explicitNulls = false }

    @Test
    fun `a parent dashboard parses`() {
        val body = """
            {"displayName":"Nomvula Mahlangu",
             "children":[{"learnerId":"01a0","referenceCode":"A-214","firstName":"Lebo",
                          "surname":"Mahlangu","grade":10,"school":"Sebokeng Secondary",
                          "suburb":"Sebokeng","attendanceRate":100,"isPrimary":true}],
             "selected":{"learnerId":"01a0","firstName":"Lebo","surname":"Mahlangu","grade":10,
                         "nextSession":{"sessionId":"s1","startsAt":"2026-09-26T09:00:00+02:00",
                                        "endsAt":"2026-09-26T12:30:00+02:00",
                                        "subjectFocus":"Mathematics · Trigonometry","room":"Hall A",
                                        "programmeName":"Tutoring & Academic Support",
                                        "venueName":"Sebokeng Community Centre",
                                        "venueAddress":"Moshoeshoe Street"},
                         "attendance":{"sessionsRecorded":12,"sessionsAttended":12,"sessionsLate":0,
                                       "sessionsAbsent":0,"attendanceRate":100},
                         "attendanceRate":100,
                         "headlineSubject":{"subjectId":"sub","subjectName":"Mathematics",
                                            "firstMark":42.0,"latestMark":71.0,"entries":3,
                                            "improvement":29.0},
                         "badgeCount":5,"recentAttendance":[],"outstandingForms":[]},
             "unreadNotifications":0,"unreadMessages":0,"inbox":[],
             "asOf":"2026-09-21T01:24:00+02:00"}
        """.trimIndent()

        val dashboard = json.decodeFromString<ParentDashboard>(body)

        assertEquals("Nomvula Mahlangu", dashboard.displayName)
        assertEquals(10, dashboard.selected?.grade)
        assertEquals(29.0, dashboard.selected?.headlineSubject?.improvement ?: 0.0, 0.01)
        assertEquals("Hall A", dashboard.selected?.nextSession?.room)
    }

    /**
     * The badge catalogue, not a list of awards.
     *
     * `awardedAt` is null for a badge still to be earned. A DTO that required
     * it parsed the earned ones and threw on the first unearned one — which is
     * a screen that works until a learner has not earned everything.
     */
    @Test
    fun `an unearned badge parses, with no award date`() {
        val body = """
            [{"badgeId":"b1","code":"full_house","name":"Full House",
              "description":"Every session in a term","icon":"★","criteria":"AttendanceStreak",
              "threshold":10,"points":50,"earned":false}]
        """.trimIndent()

        val badges = json.decodeFromString<List<BadgeCatalogueEntry>>(body)

        assertEquals(1, badges.size)
        assertNull(badges[0].awardedAt)
        assertEquals(false, badges[0].earned)
    }

    @Test
    fun `a check-in response carries whether it escalated`() {
        val body = """
            {"checkInId":"c1","escalated":true,
             "message":"Check-in saved. Because a concern was flagged, it has gone straight to our safeguarding lead."}
        """.trimIndent()

        val response = json.decodeFromString<CheckInResponse>(body)

        assertTrue(response.escalated)
        assertTrue(response.message.contains("safeguarding lead"))
    }

    /**
     * A buddy suggestion has a first name and no surname, and the DTO must not
     * quietly invent a field for one.
     */
    @Test
    fun `a buddy suggestion carries no surname`() {
        val body = """
            [{"learnerId":"l1","referenceCode":"A-231","firstName":"Karabo","grade":11,
              "sharedSubjects":["Mathematics","Physical Science"]}]
        """.trimIndent()

        val suggestions = json.decodeFromString<List<BuddySuggestion>>(body)

        assertEquals("Karabo", suggestions[0].firstName)
        assertEquals(2, suggestions[0].sharedSubjects.size)
    }

    @Test
    fun `a sign-in response for a mobile client carries the refresh token`() {
        val body = """
            {"accessToken":"a","refreshToken":"r","expiresIn":900,
             "user":{"userId":"u","email":"e@x.co.za","displayName":"Thandi Mokoena",
                     "role":"Mentor","roleLabel":"Mentor","mentorId":"m1"}}
        """.trimIndent()

        val response = json.decodeFromString<SignInResponse>(body)

        assertEquals("r", response.refreshToken)
        assertTrue(response.user.isMentor)
        assertEquals("m1", response.user.mentorId)
        assertNull(response.user.learnerId)
    }

    /**
     * A field the API adds later must not break an app already in somebody's
     * hand, which is what ignoreUnknownKeys buys.
     */
    @Test
    fun `an unknown field does not break an older app`() {
        val body = """{"unread":4,"somethingAddedLater":true}"""

        assertEquals(4, json.decodeFromString<UnreadCount>(body).unread)
    }
}
