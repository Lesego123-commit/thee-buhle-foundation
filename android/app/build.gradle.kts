plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.android)
    alias(libs.plugins.kotlin.serialization)
}

android {
    namespace = "za.co.theebuhlefoundation"
    compileSdk = libs.versions.compileSdk.get().toInt()

    defaultConfig {
        applicationId = "za.co.theebuhlefoundation"
        minSdk = libs.versions.minSdk.get().toInt()
        targetSdk = libs.versions.targetSdk.get().toInt()
        versionCode = 1
        versionName = "1.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"

        // The API's base URL is a build setting rather than a constant in the
        // source, so pointing a debug build at a laptop on the same Wi-Fi does
        // not mean editing Kotlin. 10.0.2.2 is how the emulator reaches the
        // host machine's localhost.
        buildConfigField("String", "API_BASE_URL", "\"https://theebuhlefoundation.co.za/api/\"")
    }

    buildTypes {
        debug {
            // The API as it runs on the developer's own machine.
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:5166/api/\"")
            // Cleartext is permitted in debug only, because the local API runs
            // on http. The release build has no such exemption.
            manifestPlaceholders["usesCleartextTraffic"] = "true"
        }

        // The debug app against the live Azure deployment, for demonstrations
        // and for testing on a real phone before the client's domain moves
        // across. `./gradlew installAzure`. HTTPS only, like release.
        create("azure") {
            initWith(getByName("debug"))
            buildConfigField("String", "API_BASE_URL", "\"https://theebuhle-foundation.azurewebsites.net/api/\"")
            manifestPlaceholders["usesCleartextTraffic"] = "false"
            matchingFallbacks += listOf("debug")
        }

        release {
            isMinifyEnabled = true
            isShrinkResources = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            manifestPlaceholders["usesCleartextTraffic"] = "false"
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    kotlinOptions {
        jvmTarget = "17"
    }

    buildFeatures {
        // View binding rather than findViewById: a layout that loses a view
        // becomes a compile error instead of a null at runtime.
        viewBinding = true
        buildConfig = true
    }

    testOptions {
        unitTests {
            isReturnDefaultValues = true
        }
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.appcompat)
    implementation(libs.google.material)
    implementation(libs.androidx.constraintlayout)
    implementation(libs.androidx.recyclerview)
    implementation(libs.androidx.swiperefreshlayout)

    implementation(libs.androidx.lifecycle.viewmodel.ktx)
    implementation(libs.androidx.lifecycle.runtime.ktx)
    implementation(libs.androidx.fragment.ktx)
    implementation(libs.androidx.navigation.fragment.ktx)
    implementation(libs.androidx.navigation.ui.ktx)

    implementation(libs.androidx.security.crypto)

    implementation(libs.retrofit)
    implementation(libs.retrofit.kotlinx.serialization)
    implementation(libs.okhttp)
    implementation(libs.okhttp.logging)
    implementation(libs.kotlinx.serialization.json)

    testImplementation(libs.junit)
    testImplementation(libs.kotlinx.coroutines.test)
    testImplementation(libs.okhttp.mockwebserver)

    androidTestImplementation(libs.androidx.junit)
    androidTestImplementation(libs.androidx.espresso.core)
}
