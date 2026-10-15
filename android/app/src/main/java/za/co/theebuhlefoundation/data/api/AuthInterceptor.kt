// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api

import okhttp3.Interceptor
import okhttp3.Response

/**
 * Attaches the access token to every call that needs one.
 *
 * Sign-in and refresh are skipped: one has no token yet, and the other is how
 * an expired token gets replaced. Sending a stale bearer on the refresh call
 * would have it rejected before the refresh token was ever looked at.
 */
class AuthInterceptor(private val session: SessionLike) : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()
        val path = request.url.encodedPath

        if (path.endsWith("/auth/sign-in") || path.endsWith("/auth/refresh")) {
            return chain.proceed(request)
        }

        val token = session.accessToken ?: return chain.proceed(request)

        return chain.proceed(
            request.newBuilder()
                .header("Authorization", "Bearer $token")
                .build()
        )
    }
}
