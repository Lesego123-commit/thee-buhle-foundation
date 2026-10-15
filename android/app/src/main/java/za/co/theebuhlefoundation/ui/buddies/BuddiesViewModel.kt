// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.buddies

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.BuddySuggestion
import za.co.theebuhlefoundation.data.api.dto.StudyGroupCard
import za.co.theebuhlefoundation.data.repository.PeerRepository
import za.co.theebuhlefoundation.ui.common.UiState

/**
 * Study buddies and groups.
 *
 * Note what this model cannot do. There is a [requestBuddy] and no approve, and
 * a [joinGroup] that asks rather than joins — because the API has no endpoint
 * for either, and a learner approving their own pairing is the thing the
 * safeguarding policy exists to prevent.
 */
class BuddiesViewModel(private val peers: PeerRepository) : ViewModel() {

    private val _state = MutableStateFlow<UiState<BuddiesData>>(UiState.Loading)
    val state: StateFlow<UiState<BuddiesData>> = _state.asStateFlow()

    private val _message = MutableStateFlow<String?>(null)
    val message: StateFlow<String?> = _message.asStateFlow()

    private val requested = mutableSetOf<String>()

    fun load(refreshing: Boolean = false) {
        if (!refreshing) _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                _state.value = UiState.Ready(
                    BuddiesData(peers.suggestions(), peers.groups(), requested.toSet())
                )
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    fun requestBuddy(learnerId: String, firstName: String) {
        viewModelScope.launch {
            try {
                peers.requestBuddy(learnerId)
                requested += learnerId

                _message.value = "Asked to study with $firstName. A mentor will look at it."

                load(refreshing = true)
            } catch (error: ApiError) {
                _message.value = error.message
            }
        }
    }

    fun joinGroup(groupId: String, name: String) {
        viewModelScope.launch {
            try {
                peers.joinGroup(groupId)

                _message.value = "Asked to join $name. A staff member approves every group."

                load(refreshing = true)
            } catch (error: ApiError) {
                _message.value = error.message
            }
        }
    }

    fun messageShown() {
        _message.value = null
    }
}

data class BuddiesData(
    val suggestions: List<BuddySuggestion>,
    val groups: List<StudyGroupCard>,
    val requested: Set<String>,
)
