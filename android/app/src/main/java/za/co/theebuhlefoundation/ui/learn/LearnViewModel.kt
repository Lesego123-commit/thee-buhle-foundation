// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.learn

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.ResourceCard
import za.co.theebuhlefoundation.data.api.dto.SubjectCard
import za.co.theebuhlefoundation.data.repository.LearningRepository
import za.co.theebuhlefoundation.ui.common.UiState

class LearnViewModel(private val learning: LearningRepository) : ViewModel() {

    private val _state = MutableStateFlow<UiState<LearnData>>(UiState.Loading)
    val state: StateFlow<UiState<LearnData>> = _state.asStateFlow()

    private var searchJob: Job? = null

    fun load(refreshing: Boolean = false) {
        if (!refreshing) _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                val subjects = learning.subjects()

                _state.value = if (subjects.isEmpty()) {
                    UiState.Empty(
                        "No subjects yet",
                        "Your subjects appear here once you are enrolled in a programme.",
                    )
                } else {
                    UiState.Ready(LearnData(subjects, learning.continueWatching()))
                }
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    /**
     * Debounced, so typing a subject name is one request rather than eight.
     * The previous search is cancelled outright — an answer to an abandoned
     * query arriving late would overwrite the results for what was typed after
     * it.
     */
    fun search(query: String) {
        searchJob?.cancel()

        if (query.isBlank()) {
            load()

            return
        }

        searchJob = viewModelScope.launch {
            delay(SEARCH_DEBOUNCE_MS)

            try {
                val results = learning.search(query)

                _state.value = if (results.isEmpty()) {
                    UiState.Empty("Nothing found", "Try a different word, or browse by subject.")
                } else {
                    UiState.Ready(LearnData(emptyList(), null, results))
                }
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    private companion object {
        const val SEARCH_DEBOUNCE_MS = 350L
    }
}

data class LearnData(
    val subjects: List<SubjectCard>,
    val continueWatching: ResourceCard? = null,
    val searchResults: List<ResourceCard> = emptyList(),
)
