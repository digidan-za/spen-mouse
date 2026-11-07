plugins {
    alias(libs.plugins.android.application)
}

android {
    namespace = "com.digidan.spenmouse"
    compileSdk {
        version = release(37)
    }

    defaultConfig {
        applicationId = "com.digidan.spenmouse"
        minSdk = 29
        targetSdk = 37
        versionCode = 1
        versionName = "1.0"
    }

    buildTypes {
        release {
            optimization {
                enable = true
                packageScope = setOf("androidx.**", "kotlin.**", "kotlinx.**")
            }
        }
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_11
        targetCompatibility = JavaVersion.VERSION_11
    }
}

dependencies {
    // Samsung S Pen Remote SDK: two jars from developer.samsung.com/galaxy-spen-remote, git-ignored. See README.
    implementation(fileTree("libs") { include("*.jar") })
    implementation(libs.androidx.appcompat)
    implementation(libs.androidx.core.ktx)
}