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

import kotlinx.serialization.Serializable
import kotlinx.serialization.json.Json
import java.io.IOException

/**
 * The one failure type the view models handle.
 *
 * [fields] carries the API's per-field messages so a form can bind them
 * straight onto its inputs; [status] lets a caller tell "you are not signed in"
 * apart from "that is already registered".
 *
 * **It extends IOException deliberately, and must keep doing so.** OkHttp
 * permits an interceptor to throw only an IOException; anything else is a fatal
 * exception on the dispatcher thread rather than a failure delivered to the
 * caller. Extending Exception instead crashed the app on its first unauthorised
 * request — the error was correct, and it took the process down with it.
 */
class ApiError(
    override val message: String,
    val status: Int,
    val code: String = "unknown",
    val fields: Map<String, String> = emptyMap(),
) : IOException(message) {

    /** True when the request never reached the server. */
    val isOffline: Boolean get() = status == 0

    /** True when the API refused the values rather than the request. */
    val isValidation: Boolean get() = (status == 400 || status == 422) && fields.isNotEmpty()

    companion object {
        private val json = Json { ignoreUnknownKeys = true }

        fun offline() = ApiError(
            "We could not reach Thee Buhle. Check your connection and try again.",
            status = 0,
            code = "network",
        )

        fun timeout() = ApiError(
            "That took too long. Check your connection and try again.",
            status = 0,
            code = "timeout",
        )

        /**
         * Maps an RFC 7807 problem document onto this type.
         *
         * Two shapes arrive. A model-validation failure carries `errors`, a
         * dictionary of PascalCase property names to message lists. A business
         * rule refused by the API carries `title` and `detail` and no
         * dictionary — and that detail is written for the person who will read
         * it, so it is used as-is rather than replaced with something generic.
         *
         * The dictionary keys are lower-cased on the first letter because that
         * is what the form fields are named: the server validates `Email`, the
         * input is `email`, and a message that cannot find its field is a red
         * box with no explanation in it.
         */
        fun fromProblem(body: String?, status: Int): ApiError {
            if (body.isNullOrBlank()) {
                return ApiError(generic(status), status, "http_$status")
            }

            val problem = runCatching { json.decodeFromString<ProblemDetails>(body) }.getOrNull()
                ?: return ApiError(generic(status), status, "http_$status")

            val fields = problem.errors.orEmpty()
                .mapKeys { (key, _) -> key.replaceFirstChar { it.lowercaseChar() } }
                .mapValues { (_, messages) -> messages.firstOrNull().orEmpty() }

            val message = problem.detail
                ?: if (fields.isNotEmpty()) "Please check the highlighted fields." else problem.title
                ?: generic(status)

            return ApiError(message, status, problem.title ?: "http_$status", fields)
        }

        private fun generic(status: Int) = when (status) {
            401 -> "Please sign in again."
            403 -> "You do not have permission to do that."
            404 -> "We could not find that."
            429 -> "Too many attempts. Please wait a moment and try again."
            in 500..599 -> "Something went wrong at our end. Please try again shortly."
            else -> "Something went wrong. Please try again."
        }
    }
}

@Serializable
private data class ProblemDetails(
    val title: String? = null,
    val status: Int? = null,
    val detail: String? = null,
    val errors: Map<String, List<String>>? = null,
)
