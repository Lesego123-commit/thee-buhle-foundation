// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.home

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.MentorDashboard
import za.co.theebuhlefoundation.data.api.dto.ParentDashboard
import za.co.theebuhlefoundation.data.repository.AuthRepository
import za.co.theebuhlefoundation.data.repository.LearningRepository
import za.co.theebuhlefoundation.data.repository.MentorRepository
import za.co.theebuhlefoundation.data.repository.ProgressRepository
import za.co.theebuhlefoundation.ui.common.UiState

/**
 * Home, for whichever kind of account is signed in.
 *
 * A learner and a parent see the same panel — a parent's is about a child they
 * pick — and a mentor sees their assigned learners instead. Deciding that here
 * rather than in the fragment means the screen has one state to render and no
 * branching on roles in the view.
 */
class HomeViewModel(
    private val auth: AuthRepository,
    private val learning: LearningRepository,
    private val mentor: MentorRepository,
    private val progress: ProgressRepository,
) : ViewModel() {

    private val _state = MutableStateFlow<UiState<HomeData>>(UiState.Loading)
    val state: StateFlow<UiState<HomeData>> = _state.asStateFlow()

    private var selectedLearnerId: String? = null

    fun load(refreshing: Boolean = false) {
        if (!refreshing) {
            _state.value = UiState.Loading
        }

        viewModelScope.launch {
            try {
                val user = auth.currentUser

                _state.value = if (user?.isMentor == true) {
                    UiState.Ready(HomeData.Mentor(mentor.dashboard()))
                } else {
                    UiState.Ready(HomeData.Learner(learning.dashboard(selectedLearnerId)))
                }
            } catch (error: ApiError) {
                _state.value = UiState.Failed(
                    error.message,
                    canRetry = error.status != 403,
                    isOffline = error.isOffline,
                )
            }
        }
    }

    /** A parent switching between their children. */
    fun selectLearner(learnerId: String) {
        selectedLearnerId = learnerId

        // Every other screen resolves "which learner" through the same cache,
        // so switching child here has to clear it — otherwise Learn and
        // Buddies keep showing the previous child.
        learning.learner.forget()

        load()
    }

    private val _message = MutableStateFlow<String?>(null)

    /** One-shot feedback after answering a form; cleared by [messageShown]. */
    val message: StateFlow<String?> = _message.asStateFlow()

    fun messageShown() {
        _message.value = null
    }

    /**
     * Only a guardian can answer a permission form. A learner signed in on
     * their own account still sees what is waiting, so they can remind a
     * parent, but the answer has to come from the parent's account.
     */
    val canAnswerForms: Boolean get() = auth.currentUser?.isParent == true

    /**
     * Records a guardian's answer, then reloads so the form leaves the
     * "waiting" list. The server's own confirmation is what the parent sees,
     * because it states exactly what was recorded.
     */
    fun answerForm(formId: String, learnerId: String, granted: Boolean) {
        viewModelScope.launch {
            try {
                _message.value = progress.recordConsent(formId, learnerId, granted).message

                load(refreshing = true)
            } catch (error: ApiError) {
                _message.value = error.message
            }
        }
    }

    private val _signedOut = MutableStateFlow(false)
    val signedOut: StateFlow<Boolean> = _signedOut.asStateFlow()

    fun signOut() {
        viewModelScope.launch {
            auth.signOut()

            _signedOut.value = true
        }
    }

    val displayName: String? get() = auth.currentUser?.displayName

    val roleLabel: String? get() = auth.currentUser?.roleLabel
}

sealed interface HomeData {
    data class Learner(val dashboard: ParentDashboard) : HomeData
    data class Mentor(val dashboard: MentorDashboard) : HomeData
}
