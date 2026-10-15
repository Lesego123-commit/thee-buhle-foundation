// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.subject

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.ResourceCard
import za.co.theebuhlefoundation.data.repository.LearningRepository
import za.co.theebuhlefoundation.ui.common.UiState

class SubjectViewModel(
    private val learning: LearningRepository,
    private val subjectId: String,
) : ViewModel() {

    private val _state = MutableStateFlow<UiState<List<ResourceCard>>>(UiState.Loading)
    val state: StateFlow<UiState<List<ResourceCard>>> = _state.asStateFlow()

    private val _download = MutableStateFlow<String?>(null)
    val download: StateFlow<String?> = _download.asStateFlow()

    private var filter: String? = null

    fun load() {
        _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                val resources = learning.resources(subjectId, filter)

                _state.value = if (resources.isEmpty()) {
                    UiState.Empty(
                        "Nothing here yet",
                        "Your tutor adds worksheets, videos and past papers as the term goes on.",
                    )
                } else {
                    UiState.Ready(resources)
                }
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    fun filterBy(type: String?) {
        filter = type
        load()
    }

    /**
     * Asks the API for a short-lived signed link rather than a permanent one.
     *
     * The URL carries its own authorisation and expires, so a link forwarded
     * out of the app stops working — which is the property a learning resource
     * needs when the app cannot attach a bearer token to a download the system
     * browser performs.
     */
    fun requestDownload(resourceId: String) {
        viewModelScope.launch {
            try {
                _download.value = learning.downloadLink(resourceId).url
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    fun downloadHandled() {
        _download.value = null
    }

    fun recordProgress(resourceId: String, percent: Int) {
        viewModelScope.launch { runCatching { learning.recordProgress(resourceId, percent) } }
    }
}
