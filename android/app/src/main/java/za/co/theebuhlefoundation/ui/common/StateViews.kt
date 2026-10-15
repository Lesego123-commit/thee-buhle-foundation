// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.common

import android.view.View
import android.widget.FrameLayout
import android.widget.ProgressBar
import android.widget.TextView
import com.google.android.material.button.MaterialButton
import za.co.theebuhlefoundation.R

/**
 * Drives the shared loading / failed / empty views inside a container.
 *
 * Written against the ids rather than a binding class because the layout is a
 * `<merge>` included by a dozen screens; each one would otherwise need its own
 * generated binding for the same four views.
 */
class StateViews(private val host: FrameLayout, private val content: View) {

    private val loading: ProgressBar = host.findViewById(R.id.stateLoading)
    private val message: View = host.findViewById(R.id.stateMessage)
    private val title: TextView = host.findViewById(R.id.stateTitle)
    private val body: TextView = host.findViewById(R.id.stateBody)
    private val retry: MaterialButton = host.findViewById(R.id.stateRetry)

    fun onRetry(action: () -> Unit) = retry.setOnClickListener { action() }

    fun <T> render(state: UiState<T>, onReady: (T) -> Unit) {
        loading.visibility = if (state is UiState.Loading) View.VISIBLE else View.GONE
        message.visibility = View.GONE
        content.visibility = View.GONE

        when (state) {
            is UiState.Loading -> Unit

            is UiState.Ready -> {
                content.visibility = View.VISIBLE
                onReady(state.data)
            }

            is UiState.Failed -> {
                message.visibility = View.VISIBLE
                title.text = host.context.getString(
                    if (state.isOffline) R.string.offline_title else R.string.problem_title
                )
                body.text = state.message
                retry.visibility = if (state.canRetry) View.VISIBLE else View.GONE
            }

            is UiState.Empty -> {
                message.visibility = View.VISIBLE
                title.text = state.title
                body.text = state.body
                retry.visibility = View.GONE
            }
        }
    }
}
