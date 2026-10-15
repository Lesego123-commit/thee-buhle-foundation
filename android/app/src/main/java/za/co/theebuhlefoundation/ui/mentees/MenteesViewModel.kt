// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.mentees

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.MentorDashboard
import za.co.theebuhlefoundation.data.repository.MentorRepository
import za.co.theebuhlefoundation.ui.common.UiState

class MenteesViewModel(private val mentor: MentorRepository) : ViewModel() {

    private val _state = MutableStateFlow<UiState<MentorDashboard>>(UiState.Loading)
    val state: StateFlow<UiState<MentorDashboard>> = _state.asStateFlow()

    fun load(refreshing: Boolean = false) {
        if (!refreshing) _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                val dashboard = mentor.dashboard()

                _state.value = if (dashboard.mentees.isEmpty()) {
                    UiState.Empty(
                        "No learners yet",
                        "A staff member pairs you with a learner after your induction.",
                    )
                } else {
                    // Overdue first. A mentor opening this screen should see
                    // who has not been checked on, not who was seen yesterday.
                    UiState.Ready(
                        dashboard.copy(
                            mentees = dashboard.mentees.sortedByDescending { it.daysSince ?: Int.MAX_VALUE }
                        )
                    )
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
}
