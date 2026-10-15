// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
//    - JetBrains (2024) kotlinx.serialization documentation.
// =============================================================================

package za.co.theebuhlefoundation.data.api.dto

import kotlinx.serialization.Serializable

/**
 * The API answers in camelCase and serialises enums as their names, so these
 * names match the wire format exactly and no @SerialName is needed anywhere.
 *
 * That is worth stating because it was not always true: an earlier client for
 * this system was written against a different API and used snake_case, which
 * bound to nothing and failed silently.
 */
@Serializable
data class SignInRequest(
    val email: String,
    val password: String,
    val device: String,
    /**
     * The mobile app asks for the refresh token in the body. A browser gets it
     * as an HttpOnly cookie instead; an app has no cookie jar worth relying on
     * and stores it in the OS keystore itself.
     */
    val returnRefreshTokenInBody: Boolean = true,
)

@Serializable
data class SignInResponse(
    val accessToken: String,
    val refreshToken: String? = null,
    val expiresIn: Int,
    val user: CurrentUser,
)

@Serializable
data class RefreshRequest(val refreshToken: String, val device: String? = null)

@Serializable
data class SignOutRequest(val refreshToken: String? = null)

@Serializable
data class ChangePasswordRequest(val currentPassword: String, val newPassword: String)

/**
 * A one-time code from the foundation: an invitation to a new account, or a
 * password reset. The server ignores case, spaces and the dash in the code.
 */
@Serializable
data class RedeemCodeRequest(val email: String, val code: String, val newPassword: String)

@Serializable
data class CurrentUser(
    val userId: String,
    val email: String,
    val displayName: String,
    /** "Learner", "Parent", "Mentor", "Staff", "SafeguardingLead" or "Admin". */
    val role: String,
    val roleLabel: String,
    val parentId: String? = null,
    val learnerId: String? = null,
    val mentorId: String? = null,
    val staffId: String? = null,
) {
    val isLearner: Boolean get() = role == "Learner"
    val isParent: Boolean get() = role == "Parent"
    val isMentor: Boolean get() = role == "Mentor"
}
