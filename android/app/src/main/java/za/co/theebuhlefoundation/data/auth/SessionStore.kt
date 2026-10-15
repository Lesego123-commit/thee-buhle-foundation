// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - JetBrains (2024) kotlinx.serialization documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.auth

import android.content.Context
import android.content.SharedPreferences
import androidx.security.crypto.EncryptedSharedPreferences
import androidx.security.crypto.MasterKey
import kotlinx.serialization.json.Json
import za.co.theebuhlefoundation.data.api.SessionLike
import za.co.theebuhlefoundation.data.api.dto.CurrentUser

/**
 * Where the tokens live.
 *
 * [EncryptedSharedPreferences] keeps them encrypted at rest with a key held in
 * the Android Keystore — hardware-backed on most devices — rather than in a
 * plain preferences file another app could read on a rooted phone.
 *
 * The refresh token is the valuable one. It is long-lived and it is what mints
 * new access tokens, so it never appears in a log line, never goes into a
 * bundle, and is cleared the moment a session ends.
 */
class SessionStore(context: Context) : SessionLike {

    private val prefs: SharedPreferences = EncryptedSharedPreferences.create(
        context,
        FILE_NAME,
        MasterKey.Builder(context).setKeyScheme(MasterKey.KeyScheme.AES256_GCM).build(),
        EncryptedSharedPreferences.PrefKeyEncryptionScheme.AES256_SIV,
        EncryptedSharedPreferences.PrefValueEncryptionScheme.AES256_GCM,
    )

    private val json = Json { ignoreUnknownKeys = true }

    override val accessToken: String? get() = prefs.getString(KEY_ACCESS, null)

    override val refreshToken: String? get() = prefs.getString(KEY_REFRESH, null)

    val user: CurrentUser?
        get() = prefs.getString(KEY_USER, null)
            ?.let { runCatching { json.decodeFromString<CurrentUser>(it) }.getOrNull() }

    val isSignedIn: Boolean get() = accessToken != null

    fun save(access: String, refresh: String?, user: CurrentUser) {
        prefs.edit()
            .putString(KEY_ACCESS, access)
            // A refresh that came back null leaves the stored one alone: the
            // API omits it on some responses, and treating that as "revoke the
            // session" would sign the user out at random.
            .apply { if (refresh != null) putString(KEY_REFRESH, refresh) }
            .putString(KEY_USER, json.encodeToString(CurrentUser.serializer(), user))
            .apply()
    }

    override fun updateAccess(access: String, refresh: String?) {
        prefs.edit()
            .putString(KEY_ACCESS, access)
            .apply { if (refresh != null) putString(KEY_REFRESH, refresh) }
            .apply()
    }

    override fun clear() = prefs.edit().clear().apply()

    private companion object {
        const val FILE_NAME = "tb.session"
        const val KEY_ACCESS = "access"
        const val KEY_REFRESH = "refresh"
        const val KEY_USER = "user"
    }
}
