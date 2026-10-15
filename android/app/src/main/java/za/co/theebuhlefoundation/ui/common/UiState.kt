// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.common

/**
 * What every screen in this app can be.
 *
 * A sealed type rather than three loose booleans, so a state that is both
 * loading and failed cannot be expressed — and the `when` in a fragment is
 * exhaustive, which means a new state is a compile error at every screen that
 * has not handled it.
 */
sealed interface UiState<out T> {

    data object Loading : UiState<Nothing>

    data class Ready<T>(val data: T) : UiState<T>

    /**
     * [canRetry] is false for a refusal the user cannot do anything about — a
     * 403, for instance. Offering "Try again" there is a button that will fail
     * identically every time it is pressed.
     */
    data class Failed(
        val message: String,
        val canRetry: Boolean = true,
        val isOffline: Boolean = false,
    ) : UiState<Nothing>

    data class Empty(val title: String, val body: String) : UiState<Nothing>
}
