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
 * Turns an unsuccessful response into an [ApiError] at the edge of the network
 * layer, so nothing above it ever handles a Retrofit `HttpException` or reads a
 * response body twice.
 *
 * The body is read here and thrown away, which is why it is peeked rather than
 * consumed: OkHttp's body is a one-shot stream, and reading it normally would
 * leave Retrofit with nothing to parse on the paths that do succeed.
 */
class ErrorInterceptor : Interceptor {

    override fun intercept(chain: Interceptor.Chain): Response {
        val response = chain.proceed(chain.request())

        if (response.isSuccessful) {
            return response
        }

        val body = response.peekBody(BODY_LIMIT).string()

        throw ApiError.fromProblem(body, response.code)
    }

    private companion object {
        /**
         * 64 KiB. A problem document is a few hundred bytes; anything larger is
         * a proxy's error page, and reading all of it to discover that would be
         * a waste on a metered connection.
         */
        const val BODY_LIMIT = 64L * 1024L
    }
}
