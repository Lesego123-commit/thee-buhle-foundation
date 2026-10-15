// =============================================================================
//  Code attribution (Harvard, IIE)
//
//  Own work. No third-party code was copied or adapted into this file.
//  Sources that informed it, full entries in docs/references.md:
//    - Google (2026) Android Developers documentation.
//    - Square (2024) Retrofit and OkHttp documentation.
//    - JetBrains (2024) kotlinx.serialization documentation.
// =============================================================================

package za.co.theebuhlefoundation.di

import android.content.Context
import kotlinx.serialization.json.Json
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.OkHttpClient
import retrofit2.Retrofit
import com.jakewharton.retrofit2.converter.kotlinx.serialization.asConverterFactory
import za.co.theebuhlefoundation.BuildConfig
import za.co.theebuhlefoundation.data.api.AuthInterceptor
import za.co.theebuhlefoundation.data.api.ErrorInterceptor
import za.co.theebuhlefoundation.data.api.TheeBuhleApi
import za.co.theebuhlefoundation.data.api.TokenAuthenticator
import za.co.theebuhlefoundation.data.auth.SessionStore
import za.co.theebuhlefoundation.data.repository.AuthRepository
import za.co.theebuhlefoundation.data.repository.LearningRepository
import za.co.theebuhlefoundation.data.repository.MentorRepository
import za.co.theebuhlefoundation.data.repository.MessagesRepository
import za.co.theebuhlefoundation.data.repository.PeerRepository
import za.co.theebuhlefoundation.data.repository.ProgressRepository
import java.util.concurrent.TimeUnit

/**
 * Manual dependency injection, following Google's own guidance for an app of
 * this size.
 *
 * Hilt would do the same job with annotations and an annotation processor. This
 * is one file, it is readable start to finish, it adds nothing to build time,
 * and the wiring it describes is small enough to hold in your head — which
 * matters for a project a student team hands to whoever comes after them.
 */
class AppContainer(context: Context, private val onSessionEnded: () -> Unit) {

    val session = SessionStore(context)

    private val json = Json {
        ignoreUnknownKeys = true
        explicitNulls = false
    }

    private val client: OkHttpClient = OkHttpClient.Builder()
        // 20 seconds, not the default 10. A Vaal mobile connection on a
        // Saturday morning is slow rather than absent, and a timeout that
        // fires too early looks to the user like the app is broken.
        .connectTimeout(20, TimeUnit.SECONDS)
        .readTimeout(20, TimeUnit.SECONDS)
        .addInterceptor(AuthInterceptor(session))
        .addInterceptor(ErrorInterceptor())
        .authenticator(TokenAuthenticator(session, BuildConfig.API_BASE_URL, onSessionEnded))
        .build()

    private val api: TheeBuhleApi = Retrofit.Builder()
        .baseUrl(BuildConfig.API_BASE_URL)
        .client(client)
        .addConverterFactory(json.asConverterFactory("application/json".toMediaType()))
        .build()
        .create(TheeBuhleApi::class.java)

    val auth = AuthRepository(api, session)
    val learning = LearningRepository(api, session)
    val progress = ProgressRepository(api, session)
    val peers = PeerRepository(api, learning.learner)
    val messages = MessagesRepository(api)
    val mentor = MentorRepository(api)
}
