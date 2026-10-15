// Root build file. Plugins are declared here and applied in the module that
// needs them, so a version is stated once for the whole build.
plugins {
    alias(libs.plugins.android.application) apply false
    alias(libs.plugins.kotlin.android) apply false
    alias(libs.plugins.kotlin.serialization) apply false
}
