// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.badges

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.BadgeCatalogueEntry
import za.co.theebuhlefoundation.data.api.dto.Milestone
import za.co.theebuhlefoundation.data.repository.AuthRepository
import za.co.theebuhlefoundation.data.repository.LearningRepository
import za.co.theebuhlefoundation.data.repository.ProgressRepository
import za.co.theebuhlefoundation.ui.common.UiState

class BadgesViewModel(
    private val auth: AuthRepository,
    private val learning: LearningRepository,
    private val progress: ProgressRepository,
    private val learnerId: String?,
) : ViewModel() {

    private val _state = MutableStateFlow<UiState<BadgesData>>(UiState.Loading)
    val state: StateFlow<UiState<BadgesData>> = _state.asStateFlow()

    fun load() {
        _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                val id = learnerId
                    ?: auth.currentUser?.learnerId
                    ?: learning.dashboard().selected?.learnerId

                if (id == null) {
                    _state.value = UiState.Empty(
                        "No learner yet",
                        "Badges appear once a learner is linked to this account.",
                    )

                    return@launch
                }

                _state.value = UiState.Ready(
                    BadgesData(progress.badges(id), progress.milestone(id))
                )
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }
}

/**
 * Earned first, then the rest.
 *
 * The catalogue arrives in its own order; showing what a learner has before
 * what they have not is the difference between a wall of achievement and a
 * list of things they are missing.
 */
data class BadgesData(val badges: List<BadgeCatalogueEntry>, val milestone: Milestone) {
    val earned: List<BadgeCatalogueEntry> get() = badges.filter { it.earned }
    val remaining: List<BadgeCatalogueEntry> get() = badges.filterNot { it.earned }
}
