// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api

/**
 * The part of the session the network layer needs.
 *
 * Extracted as an interface so the interceptor and the authenticator can be
 * exercised on the JVM. The real implementation is backed by
 * EncryptedSharedPreferences, which needs a device — and the behaviour worth
 * testing here is the token rotation, not the Android Keystore.
 */
interface SessionLike {

    val accessToken: String?

    val refreshToken: String?

    fun updateAccess(access: String, refresh: String?)

    fun clear()
}
