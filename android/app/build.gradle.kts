import java.util.Properties

// Release signing: android/keystore.properties + the .jks it names, both git-ignored. Without them you still get a debug build.
val keystoreProps = Properties().apply {
    rootProject.file("keystore.properties").takeIf { it.exists() }?.inputStream()?.use { load(it) }
}

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
        versionCode = 3
        versionName = "0.2.1"
    }

    signingConfigs {
        if (keystoreProps.isNotEmpty()) create("release") {
            storeFile = rootProject.file(keystoreProps["storeFile"] as String)
            storePassword = keystoreProps["storePassword"] as String
            keyAlias = keystoreProps["keyAlias"] as String
            keyPassword = keystoreProps["keyPassword"] as String
        }
    }
    buildTypes {
        release {
            signingConfig = signingConfigs.findByName("release")
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