// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui.common

import androidx.fragment.app.Fragment
import androidx.lifecycle.ViewModel
import androidx.lifecycle.ViewModelProvider
import za.co.theebuhlefoundation.TheeBuhleApplication
import za.co.theebuhlefoundation.di.AppContainer

/**
 * Hands the container to a view model.
 *
 * The alternative is a view model that reaches for a singleton, which is what
 * makes a view model untestable — the point of MVVM is that the model can be
 * exercised without an Android device, and it cannot be if it fetches its own
 * dependencies from a static.
 */
class ContainerViewModelFactory(
    private val container: AppContainer,
    private val create: (AppContainer) -> ViewModel,
) : ViewModelProvider.Factory {

    @Suppress("UNCHECKED_CAST")
    override fun <T : ViewModel> create(modelClass: Class<T>): T = create(container) as T
}

/** The container, from any fragment. */
val Fragment.container: AppContainer
    get() = (requireActivity().application as TheeBuhleApplication).container
