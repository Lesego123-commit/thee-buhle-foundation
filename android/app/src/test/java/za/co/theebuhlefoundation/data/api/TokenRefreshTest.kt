// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
//    - JUnit (2021) JUnit 4 documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api

import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.mockwebserver.MockResponse
import okhttp3.mockwebserver.MockWebServer
import org.junit.After
import org.junit.Assert.assertEquals
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Before
import org.junit.Test
import za.co.theebuhlefoundation.data.api.dto.CurrentUser

/**
 * Token refresh, against a real HTTP server.
 *
 * MockWebServer rather than a mocked OkHttp: the behaviour under test is
 * OkHttp's own — that an [okhttp3.Authenticator] is consulted after a 401 and
 * the original request is then retried — and mocking the library would only
 * assert that the mock does what the test says it does.
 */
class TokenRefreshTest {

    private lateinit var server: MockWebServer
    private lateinit var session: FakeSessionStore

    @Before
    fun start() {
        server = MockWebServer()
        server.start()
        session = FakeSessionStore(access = "expired", refresh = "refresh-1")
    }

    @After
    fun stop() {
        server.shutdown()
    }

    private fun client(onEnded: () -> Unit = {}): OkHttpClient =
        OkHttpClient.Builder()
            .addInterceptor(AuthInterceptor(session))
            .authenticator(TokenAuthenticator(session, server.url("/api/").toString(), onEnded))
            .build()

    @Test
    fun `a 401 refreshes the token and retries the original request`() {
        server.enqueue(MockResponse().setResponseCode(401))
        server.enqueue(
            MockResponse().setBody(
                """{"accessToken":"fresh","refreshToken":"refresh-2","expiresIn":900,
                   "user":{"userId":"u","email":"e","displayName":"d","role":"Learner","roleLabel":"Learner"}}"""
            )
        )
        server.enqueue(MockResponse().setBody("""{"ok":true}"""))

        val response = client().newCall(
            Request.Builder().url(server.url("/api/dashboard")).build()
        ).execute()

        assertEquals(200, response.code)

        // The rotated pair was kept: presenting a used refresh token again
        // revokes the whole family server-side.
        assertEquals("fresh", session.access)
        assertEquals("refresh-2", session.refresh)

        val first = server.takeRequest()
        val refresh = server.takeRequest()
        val retried = server.takeRequest()

        assertEquals("Bearer expired", first.getHeader("Authorization"))
        assertEquals("/api/auth/refresh", refresh.path)
        assertEquals("Bearer fresh", retried.getHeader("Authorization"))
    }

    @Test
    fun `a refused refresh ends the session instead of looping`() {
        server.enqueue(MockResponse().setResponseCode(401))
        server.enqueue(MockResponse().setResponseCode(401))

        var ended = false

        val response = client { ended = true }.newCall(
            Request.Builder().url(server.url("/api/dashboard")).build()
        ).execute()

        assertEquals(401, response.code)
        assertTrue("the session should have ended", ended)
        assertNull(session.access)
        assertNull(session.refresh)

        // Two calls: the original and one refresh attempt. Not a third.
        assertEquals(2, server.requestCount)
    }

    @Test
    fun `the refresh call itself carries no bearer token`() {
        server.enqueue(MockResponse().setResponseCode(401))
        server.enqueue(
            MockResponse().setBody(
                """{"accessToken":"fresh","refreshToken":"r2","expiresIn":900,
                   "user":{"userId":"u","email":"e","displayName":"d","role":"Learner","roleLabel":"Learner"}}"""
            )
        )
        server.enqueue(MockResponse().setBody("{}"))

        client().newCall(Request.Builder().url(server.url("/api/dashboard")).build()).execute()

        server.takeRequest()

        // A stale bearer on the refresh call would be rejected before the
        // refresh token was ever looked at.
        assertNull(server.takeRequest().getHeader("Authorization"))
    }

    @Test
    fun `sign-in is not given a token it does not need`() {
        server.enqueue(MockResponse().setBody("{}"))

        client().newCall(
            Request.Builder().url(server.url("/api/auth/sign-in")).build()
        ).execute()

        assertNull(server.takeRequest().getHeader("Authorization"))
    }

    @Test
    fun `a request with no session goes out unauthenticated rather than failing`() {
        session.clear()
        server.enqueue(MockResponse().setBody("{}"))

        val response = client().newCall(
            Request.Builder().url(server.url("/api/public/programmes")).build()
        ).execute()

        assertEquals(200, response.code)
        assertNull(server.takeRequest().getHeader("Authorization"))
    }
}

/** A session store that holds its values in memory, for tests off-device. */
private class FakeSessionStore(
    var access: String? = null,
    var refresh: String? = null,
) : SessionLike {

    override val accessToken: String? get() = access
    override val refreshToken: String? get() = refresh

    override fun updateAccess(access: String, refresh: String?) {
        this.access = access
        if (refresh != null) this.refresh = refresh
    }

    override fun clear() {
        access = null
        refresh = null
    }
}
