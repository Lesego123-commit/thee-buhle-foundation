// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.notifications

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.NotificationItem
import za.co.theebuhlefoundation.data.repository.MessagesRepository
import za.co.theebuhlefoundation.ui.common.UiState

class NotificationsViewModel(private val messages: MessagesRepository) : ViewModel() {

    private val _state = MutableStateFlow<UiState<List<NotificationItem>>>(UiState.Loading)
    val state: StateFlow<UiState<List<NotificationItem>>> = _state.asStateFlow()

    private val _unread = MutableStateFlow(0)
    val unread: StateFlow<Int> = _unread.asStateFlow()

    fun load(refreshing: Boolean = false) {
        if (!refreshing) _state.value = UiState.Loading

        viewModelScope.launch {
            try {
                val items = messages.notifications()

                _unread.value = items.count { it.readAt == null }

                _state.value = if (items.isEmpty()) {
                    UiState.Empty(
                        "Nothing yet",
                        "Reminders about sessions, badges and permission forms appear here.",
                    )
                } else {
                    UiState.Ready(items)
                }
            } catch (error: ApiError) {
                _state.value = UiState.Failed(error.message, isOffline = error.isOffline)
            }
        }
    }

    fun markRead(id: String) {
        viewModelScope.launch {
            runCatching { messages.markNotificationRead(id) }
            load(refreshing = true)
        }
    }

    fun markAllRead() {
        viewModelScope.launch {
            runCatching { messages.markAllRead() }
            load(refreshing = true)
        }
    }
}
