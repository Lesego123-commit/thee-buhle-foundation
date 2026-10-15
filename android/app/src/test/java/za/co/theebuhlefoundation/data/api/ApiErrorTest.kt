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

import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

/**
 * The mapping from the API's problem documents onto the one error type the
 * screens handle.
 *
 * These matter more than they look. A message that does not reach the field it
 * is about is a red box with no explanation in it, and a message the client
 * invents in place of the server's is worse than either.
 */
class ApiErrorTest {

    @Test
    fun `a business rule keeps the API's own words`() {
        val problem = """
            {
              "title": "rule_violation",
              "status": 409,
              "detail": "We cannot register you without your consent to hold these details."
            }
        """.trimIndent()

        val error = ApiError.fromProblem(problem, 409)

        assertEquals(
            "We cannot register you without your consent to hold these details.",
            error.message,
        )
        assertEquals(409, error.status)
        assertFalse(error.isValidation)
    }

    @Test
    fun `a validation failure binds each message to its field`() {
        val problem = """
            {
              "title": "One or more validation errors occurred.",
              "status": 400,
              "errors": {
                "Email": ["That does not look like an email address. Please check it."],
                "Body": ["Please tell us a little more — at least a sentence."]
              }
            }
        """.trimIndent()

        val error = ApiError.fromProblem(problem, 400)

        assertTrue(error.isValidation)

        // Lower-cased on the first letter, because that is what the form
        // controls are named: the server validates Email, the input is email.
        assertEquals(
            "That does not look like an email address. Please check it.",
            error.fields["email"],
        )
        assertEquals(
            "Please tell us a little more — at least a sentence.",
            error.fields["body"],
        )
    }

    @Test
    fun `a validation failure summarises rather than repeating the framework's title`() {
        val problem = """
            {"title":"One or more validation errors occurred.","status":400,
             "errors":{"Email":["Required."]}}
        """.trimIndent()

        val error = ApiError.fromProblem(problem, 400)

        assertEquals("Please check the highlighted fields.", error.message)
    }

    @Test
    fun `a body that is not a problem document still produces something readable`() {
        val error = ApiError.fromProblem("<html>502 Bad Gateway</html>", 502)

        assertEquals(502, error.status)
        assertTrue(error.message.isNotBlank())
        assertFalse(error.message.contains("<html", ignoreCase = true))
    }

    @Test
    fun `an empty body produces a message for the status`() {
        assertTrue(ApiError.fromProblem(null, 403).message.contains("permission"))
        assertTrue(ApiError.fromProblem("", 429).message.contains("Too many"))
    }

    /**
     * OkHttp permits an interceptor to throw only an IOException. Anything else
     * is a fatal exception on the dispatcher thread — which crashed this app on
     * its first unauthorised request.
     */
    @Test
    fun `it is an IOException, which is what lets an interceptor throw it`() {
        assertTrue(ApiError.offline() is java.io.IOException)
    }

    @Test
    fun `a network failure is marked offline rather than as a status`() {
        assertTrue(ApiError.offline().isOffline)
        assertTrue(ApiError.timeout().isOffline)
        assertFalse(ApiError.fromProblem(null, 500).isOffline)
    }
}
