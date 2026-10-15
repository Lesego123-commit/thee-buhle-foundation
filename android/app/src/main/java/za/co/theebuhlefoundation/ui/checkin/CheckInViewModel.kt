// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
//    - Republic of South Africa (2005) Children's Act 38 of 2005.
// =============================================================================

package za.co.theebuhlefoundation.ui.checkin

import androidx.lifecycle.ViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.launch
import za.co.theebuhlefoundation.data.api.ApiError
import za.co.theebuhlefoundation.data.api.dto.LogCheckInRequest
import za.co.theebuhlefoundation.data.repository.MentorRepository
import java.time.LocalDate

/**
 * Logging a mentor check-in.
 *
 * The screen a mentor uses when something is wrong, which makes two of its
 * behaviours load-bearing rather than cosmetic:
 *
 *  * **A 503 is not a failure to retry.** It means the concern could not be
 *    routed to anybody and *nothing was saved* — the API refuses to record a
 *    concern that would reach no one. The mentor is told to phone instead, and
 *    the form keeps what they typed.
 *  * **An escalated check-in disappears.** When the API reports `escalated`,
 *    the note is no longer visible to the mentor who wrote it, including in
 *    their own history. The response says so and this screen repeats it,
 *    because a mentor who thinks their note vanished will write it again
 *    somewhere less safe.
 */
class CheckInViewModel(
    private val mentor: MentorRepository,
    private val assignmentId: String,
) : ViewModel() {

    private val _state = MutableStateFlow(CheckInState())
    val state: StateFlow<CheckInState> = _state.asStateFlow()

    fun save(
        wellbeing: String,
        mode: String,
        supportAreas: List<String>,
        notes: String,
        flagged: Boolean,
    ) {
        _state.value = _state.value.copy(busy = true, error = null)

        viewModelScope.launch {
            try {
                val response = mentor.logCheckIn(
                    LogCheckInRequest(
                        assignmentId = assignmentId,
                        occurredOn = LocalDate.now().toString(),
                        mode = mode,
                        wellbeing = wellbeing,
                        notes = notes.ifBlank { null },
                        supportAreas = supportAreas,
                        concernFlagged = flagged,
                    )
                )

                _state.value = CheckInState(
                    saved = true,
                    escalated = response.escalated,
                    message = response.message,
                )
            } catch (error: ApiError) {
                _state.value = CheckInState(
                    // 503 here is the unroutable-concern case. Nothing was
                    // saved, and the API's own words say what to do instead.
                    error = error.message,
                    unroutable = error.status == 503,
                )
            }
        }
    }
}

data class CheckInState(
    val busy: Boolean = false,
    val saved: Boolean = false,
    val escalated: Boolean = false,
    val message: String? = null,
    val error: String? = null,
    val unroutable: Boolean = false,
)
