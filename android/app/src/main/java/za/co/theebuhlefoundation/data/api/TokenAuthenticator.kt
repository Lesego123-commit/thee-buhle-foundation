// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
//    - JetBrains (2024) kotlinx.serialization documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api

import kotlinx.coroutines.runBlocking
import kotlinx.serialization.json.Json
import okhttp3.Authenticator
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import okhttp3.Request
import okhttp3.RequestBody.Companion.toRequestBody
import okhttp3.Response
import okhttp3.Route
import za.co.theebuhlefoundation.data.api.dto.RefreshRequest
import za.co.theebuhlefoundation.data.api.dto.SignInResponse

/**
 * Renews an expired access token once, when a call comes back 401.
 *
 * An OkHttp [Authenticator] rather than an interceptor, because OkHttp calls it
 * *after* a 401 and then retries the original request itself — so a screen
 * whose token expired mid-scroll reloads rather than bouncing the user to the
 * sign-in screen.
 *
 * Three properties matter here:
 *
 *  * **It gives up after one attempt.** `response.priorResponse` is checked, so
 *    a refresh token that is itself rejected ends the session instead of
 *    starting a loop against the API.
 *  * **It is synchronised.** Four screens loading at once produce four 401s;
 *    without the lock they would each spend the same single-use refresh token,
 *    and the API revokes the whole family when a used one is presented again.
 *  * **It uses its own client.** Refreshing through the main client would run
 *    this authenticator again on the refresh call.
 */
class TokenAuthenticator(
    private val session: SessionLike,
    private val baseUrl: String,
    private val onSessionEnded: () -> Unit,
) : Authenticator {

    private val plainClient = OkHttpClient.Builder().build()
    private val json = Json { ignoreUnknownKeys = true }
    private val lock = Any()

    override fun authenticate(route: Route?, response: Response): Request? {
        // Already retried once. The refresh did not help, so stop.
        if (response.priorResponse != null) {
            endSession()

            return null
        }

        val presented = response.request.header("Authorization")

        synchronized(lock) {
            val current = session.accessToken ?: run { endSession(); return null }

            // Another thread refreshed while this one waited on the lock. Retry
            // with what it got rather than spending the refresh token again.
            if (presented != null && presented != "Bearer $current") {
                return response.request.newBuilder()
                    .header("Authorization", "Bearer $current")
                    .build()
            }

            val refresh = session.refreshToken ?: run { endSession(); return null }
            val renewed = runBlocking { renew(refresh) } ?: run { endSession(); return null }

            session.updateAccess(renewed.accessToken, renewed.refreshToken)

            return response.request.newBuilder()
                .header("Authorization", "Bearer ${renewed.accessToken}")
                .build()
        }
    }

    private fun renew(refreshToken: String): SignInResponse? {
        val body = json.encodeToString(
            RefreshRequest.serializer(),
            RefreshRequest(refreshToken, device = "Android"),
        ).toRequestBody("application/json".toMediaType())

        val request = Request.Builder()
            .url("${baseUrl}auth/refresh")
            .post(body)
            .build()

        return runCatching {
            plainClient.newCall(request).execute().use { response ->
                if (!response.isSuccessful) return null

                response.body?.string()?.let { json.decodeFromString<SignInResponse>(it) }
            }
        }.getOrNull()
    }

    private fun endSession() {
        session.clear()
        onSessionEnded()
    }
}
