// Repositories are declared once, here, and modules may not add their own. A
// dependency can therefore never be pulled from somewhere nobody vetted.
pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "TheeBuhle"
include(":app")
