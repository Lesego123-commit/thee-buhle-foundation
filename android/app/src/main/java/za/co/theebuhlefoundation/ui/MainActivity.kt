// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Google (2026) Material Design 3.
// =============================================================================

package za.co.theebuhlefoundation.ui

import android.os.Bundle
import android.view.View
import androidx.appcompat.app.AppCompatActivity
import androidx.core.view.ViewCompat
import androidx.core.view.WindowInsetsCompat
import androidx.core.view.updatePadding
import androidx.navigation.NavController
import androidx.navigation.fragment.NavHostFragment
import androidx.navigation.ui.setupWithNavController
import com.google.android.material.snackbar.Snackbar
import za.co.theebuhlefoundation.R
import za.co.theebuhlefoundation.TheeBuhleApplication
import za.co.theebuhlefoundation.databinding.ActivityMainBinding

/**
 * The single activity. Every screen is a fragment inside it.
 *
 * The bottom navigation changes with the signed-in role: a learner or parent
 * gets Home, Learn, Buddies, Progress and Messages; a mentor gets Home,
 * Mentees, Alerts and Account. Building the menu from the role rather than
 * hiding items means a mentor's app has no tabs they cannot use.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private lateinit var navController: NavController

    private val container get() = (application as TheeBuhleApplication).container

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)

        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        applyWindowInsets()

        val host = supportFragmentManager
            .findFragmentById(R.id.navHost) as NavHostFragment

        navController = host.navController

        // The graph is inflated here rather than in the layout so its start
        // destination can be chosen. Declaring Home as the start and then
        // navigating away from it means Home is created first and asks the API
        // for a dashboard with no token — which is a crash on launch, not a
        // redirect.
        val graph = navController.navInflater.inflate(R.navigation.nav_graph)

        graph.setStartDestination(
            if (container.auth.isSignedIn) R.id.homeFragment else R.id.signInFragment
        )

        navController.graph = graph

        // The tab bar belongs to the signed-in screens only, so it is hidden on
        // sign-in rather than drawn over it.
        navController.addOnDestinationChangedListener { _, destination, _ ->
            binding.bottomNav.visibility =
                if (destination.id == R.id.signInFragment || destination.id == R.id.activateFragment) View.GONE else View.VISIBLE
        }

        applyMenuForRole()

        if (intent.getBooleanExtra(EXTRA_SESSION_ENDED, false)) {
            Snackbar.make(
                binding.root,
                getString(R.string.session_ended),
                Snackbar.LENGTH_LONG,
            ).show()
        }
    }

    /**
     * Keeps content out from under the status and navigation bars.
     *
     * Android 15 onwards draws every app edge-to-edge whether it asks to or
     * not, so without this the ink header runs under the clock and the tab bar
     * sits under the gesture handle. The insets are applied as padding rather
     * than a margin so the ink band still paints behind the status bar, which
     * is the look the design wants.
     */
    private fun applyWindowInsets() {
        ViewCompat.setOnApplyWindowInsetsListener(binding.navHost) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())

            view.updatePadding(top = bars.top)

            insets
        }

        ViewCompat.setOnApplyWindowInsetsListener(binding.bottomNav) { view, insets ->
            val bars = insets.getInsets(WindowInsetsCompat.Type.systemBars())

            view.updatePadding(bottom = bars.bottom)

            insets
        }
    }

    /** Called after a sign-in, when the role — and so the tab bar — may differ. */
    fun applyMenuForRole() {
        val user = container.auth.currentUser

        binding.bottomNav.menu.clear()
        binding.bottomNav.inflateMenu(
            when {
                user?.isMentor == true -> R.menu.nav_mentor
                user?.isParent == true -> R.menu.nav_parent
                else -> R.menu.nav_learner
            }
        )

        binding.bottomNav.setupWithNavController(navController)
    }

    companion object {
        const val EXTRA_SESSION_ENDED = "session_ended"
    }
}
