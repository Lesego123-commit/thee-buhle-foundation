// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.signin

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.repository.AuthRepository

/**
 * The sign-in screen's model.
 *
 * It holds the whole state of the screen and the fragment renders it. Nothing
 * here touches a View, which is what lets the sign-in rules be tested without
 * an Android device.
 */
class SignInViewModel(private val auth: AuthRepository) : ViewModel() {

    private val _state = MutableStateFlow(SignInState())
    val state: StateFlow<SignInState> = _state.asStateFlow()

    fun signIn(email: String, password: String) {
        if (email.isBlank() || password.isBlank()) {
            _state.value = SignInState(
                emailError = if (email.isBlank()) "Please enter your email address." else null,
                passwordError = if (password.isBlank()) "Please enter your password." else null,
            )

            return
        }

        _state.value = SignInState(busy = true)

        viewModelScope.launch {
            try {
                auth.signIn(email, password)

                _state.value = SignInState(signedIn = true)
            } catch (error: ApiError) {
                _state.value = SignInState(
                    // The API answers identically for a wrong password, an
                    // unknown address and a locked account, so this message is
                    // whatever it said — the app must not add a distinction the
                    // API deliberately refuses to make.
                    message = error.message,
                    emailError = error.fields["email"],
                    passwordError = error.fields["password"],
                    isOffline = error.isOffline,
                )
            }
        }
    }

    fun messageShown() {
        _state.value = _state.value.copy(message = null)
    }
}

data class SignInState(
    val busy: Boolean = false,
    val signedIn: Boolean = false,
    val message: String? = null,
    val emailError: String? = null,
    val passwordError: String? = null,
    val isOffline: Boolean = false,
)
