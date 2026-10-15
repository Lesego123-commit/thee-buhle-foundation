// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
// =============================================================================

package za.co.theebuhlefoundation

import android.app.Application
import android.content.Intent
import za.co.theebuhlefoundation.di.AppContainer
import za.co.theebuhlefoundation.ui.MainActivity

class TheeBuhleApplication : Application() {

    lateinit var container: AppContainer
        private set

    override fun onCreate() {
        super.onCreate()

        container = AppContainer(this) { returnToSignIn() }
    }

    /**
     * Called when a refresh token is refused, which means the session is over —
     * the account was suspended, the password changed, or the token was
     * revoked from another device.
     *
     * The task is cleared so the back button cannot walk into a screen full of
     * somebody else's data.
     */
    private fun returnToSignIn() {
        val intent = Intent(this, MainActivity::class.java)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TASK)
            .putExtra(MainActivity.EXTRA_SESSION_ENDED, true)

        startActivity(intent)
    }
}
