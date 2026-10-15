// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.activate

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.repository.AuthRepository

/**
 * "I have a code": accepting an invitation, or finishing a password reset.
 *
 * There is no sign-up screen in this app, on purpose. The foundation creates
 * every account, because an account here shows a child's records and only
 * the foundation can say who is whose parent. The person then uses the code
 * they were given to choose their own password.
 */
class ActivateViewModel(private val auth: AuthRepository) : ViewModel() {

    private val _state = MutableStateFlow(ActivateState())
    val state: StateFlow<ActivateState> = _state.asStateFlow()

    fun submit(email: String, code: String, password: String, confirm: String) {
        val problems = validate(email, code, password, confirm)

        if (problems != null) {
            _state.value = problems

            return
        }

        _state.value = ActivateState(busy = true)

        viewModelScope.launch {
            try {
                auth.redeemCode(email, code, password)

                _state.value = ActivateState(doneFor = email.trim())
            } catch (error: ApiError) {
                // The API gives one message for every refusal (unknown email,
                // wrong code, expired, used), so this shows whatever it said.
                _state.value = ActivateState(
                    message = error.message,
                    emailError = error.fields["email"],
                    codeError = error.fields["code"],
                    passwordError = error.fields["newPassword"],
                )
            }
        }
    }

    fun messageShown() {
        _state.value = _state.value.copy(message = null)
    }

    companion object {
        const val MIN_PASSWORD = 12

        /**
         * The checks the phone can make before asking the server. Returns the
         * state to show, or null when the form is ready to send.
         */
        fun validate(email: String, code: String, password: String, confirm: String): ActivateState? {
            val emailError = when {
                email.isBlank() -> "Please enter your email address."
                !email.trim().contains('@') -> "That does not look like an email address."
                else -> null
            }

            // Ten characters once the dash and spaces are ignored, as issued.
            val codeError = when (code.count { it.isLetterOrDigit() }) {
                0 -> "Please enter the code the foundation gave you."
                10 -> null
                else -> "The code has ten letters and numbers."
            }

            val passwordError = if (password.length < MIN_PASSWORD) {
                "Use at least $MIN_PASSWORD characters. A short sentence is easy to remember."
            } else {
                null
            }

            val confirmError = if (passwordError == null && confirm != password) {
                "This does not match the password above."
            } else {
                null
            }

            return if (listOf(emailError, codeError, passwordError, confirmError).all { it == null }) {
                null
            } else {
                ActivateState(
                    emailError = emailError,
                    codeError = codeError,
                    passwordError = passwordError,
                    confirmError = confirmError,
                )
            }
        }
    }
}

data class ActivateState(
    val busy: Boolean = false,
    /** Set once the password is saved: the email to pre-fill on the sign-in screen. */
    val doneFor: String? = null,
    val message: String? = null,
    val emailError: String? = null,
    val codeError: String? = null,
    val passwordError: String? = null,
    val confirmError: String? = null,
)
