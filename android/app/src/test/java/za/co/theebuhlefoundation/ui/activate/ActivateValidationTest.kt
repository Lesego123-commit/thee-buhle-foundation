// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
//    - JUnit (2021) JUnit 4 documentation.
// =============================================================================

package za.co.theebuhlefoundation.ui.activate

import org.junit.Assert.assertEquals
import org.junit.Assert.assertNotNull
import org.junit.Assert.assertNull
import org.junit.Test

/**
 * The checks the phone makes before sending a code to the server.
 *
 * They exist to catch typing mistakes early with a message about the field,
 * not to decide anything: the server checks the code itself, and gives one
 * answer for every kind of refusal.
 */
class ActivateValidationTest {

    private val good = arrayOf("lesego@example.co.za", "K7MPQ-2XRT9", "Saturday tutoring at nine", "Saturday tutoring at nine")

    private fun check(
        email: String = good[0],
        code: String = good[1],
        password: String = good[2],
        confirm: String = good[3],
    ) = ActivateViewModel.validate(email, code, password, confirm)

    @Test
    fun `a complete form is ready to send`() {
        assertNull(check())
    }

    @Test
    fun `the code may be typed with spaces, no dash, or in lower case`() {
        assertNull(check(code = "k7mpq 2xrt9"))
        assertNull(check(code = "K7MPQ2XRT9"))
    }

    @Test
    fun `a code of the wrong length is caught before the server sees it`() {
        assertNotNull(check(code = "K7MPQ-2XRT")?.codeError)
        assertNotNull(check(code = "")?.codeError)
    }

    @Test
    fun `a short password is refused`() {
        val state = check(password = "short", confirm = "short")

        assertNotNull(state?.passwordError)
        // Only one message at a time about the password: "too short" first.
        assertNull(state?.confirmError)
    }

    @Test
    fun `the two passwords must match`() {
        assertEquals(
            "This does not match the password above.",
            check(confirm = "Saturday tutoring at ten")?.confirmError,
        )
    }

    @Test
    fun `an email address needs an at sign`() {
        assertNotNull(check(email = "lesego.example.co.za")?.emailError)
        assertNotNull(check(email = " ")?.emailError)
    }
}
