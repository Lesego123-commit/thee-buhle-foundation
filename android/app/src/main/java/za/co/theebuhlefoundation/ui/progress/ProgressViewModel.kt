// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.progress

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.LearnerProgress
import za.co.theebuhlefoundation.data.repository.AuthRepository
import za.co.theebuhlefoundation.data.repository.LearningRepository
import za.co.theebuhlefoundation.data.repository.ProgressRepository
import za.co.theebuhlefoundation.ui.common.UiState

class ProgressViewModel(
    private val auth: AuthRepository,
    private val learning: LearningRepository,
    private val progress: ProgressRepository,
) : ViewModel() {

    private val _state = MutableStateFlow<UiState<LearnerProgress>>(UiState.Loading)
    val state: StateFlow<UiState<LearnerProgress>> = _state.asStateFlow()

    var learnerId: String? = null
        private set

    fun load(refreshing: Boolean = false) {
        if (!refreshing) _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                // A learner has their own id on the account. A parent does not,
                // so the child currently selected on the dashboard is resolved
                // first — asking the API for "my progress" as a parent is a
                // question with no answer.
                val id = auth.currentUser?.learnerId
                    ?: learning.dashboard().selected?.learnerId

                if (id == null) {
                    _state.value = UiState.Empty(
                        "No learner yet",
                        "Once a learner is linked to this account, their progress appears here.",
                    )

                    return@launch
                }

                learnerId = id

                _state.value = UiState.Ready(progress.progress(id))
            } catch (error: ApiError) {
                _state.value = UiState.Failed(
                    error.message,
                    canRetry = error.status != 403,
                    isOffline = error.isOffline,
                )
            }
        }
    }
}
