// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.messages

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.InboxMessage
import za.co.theebuhlefoundation.data.repository.MessagesRepository
import za.co.theebuhlefoundation.ui.common.UiState

class MessagesViewModel(private val messages: MessagesRepository) : ViewModel() {

    private val _state = MutableStateFlow<UiState<List<InboxMessage>>>(UiState.Loading)
    val state: StateFlow<UiState<List<InboxMessage>>> = _state.asStateFlow()

    fun load(refreshing: Boolean = false) {
        if (!refreshing) _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                val inbox = messages.messages()

                _state.value = if (inbox.isEmpty()) {
                    UiState.Empty(
                        "No messages",
                        "Announcements from Thee Buhle appear here.",
                    )
                } else {
                    UiState.Ready(inbox)
                }
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    /**
     * Marked read optimistically, and not reloaded if it fails.
     *
     * The read receipt is a convenience; a network blip should not make a
     * message the user has plainly just opened spring back to unread under
     * their finger.
     */
    fun markRead(announcementId: String) {
        val current = _state.value

        if (current is UiState.Ready) {
            _state.value = UiState.Ready(
                current.data.map {
                    if (it.announcementId == announcementId && it.readAt == null) {
                        it.copy(readAt = "now")
                    } else {
                        it
                    }
                }
            )
        }

        viewModelScope.launch { runCatching { messages.markMessageRead(announcementId) } }
    }
}
