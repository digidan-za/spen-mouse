# Build log

## 2026-11-07
Project created. Nothing built yet.

Windows receiver built and running. `windows/Program.cs`, 81 lines of C#, .NET 8. Listens on UDP 51516,
exponential smoothing (default 0.5), gain 40 px per unit of delta, sub-pixel remainder carried between
packets so slow motion isn't truncated to zero. A press shorter than 250 ms on button-up is a left click.
`windows/fake-pen.ps1` pretends to be the phone: traces a circle, then a quick press.

Tested on the laptop: motion works, the circle shows up on screen. The click path was not run on purpose
(it would click whatever is under the cursor in an unattended session); verify it by hand with `.\fake-pen.ps1`.

- "It was sure and wrong" #1: the first fake-pen run moved nothing. The receiver logged `? M 0,300 0,000`
  on every line. PowerShell's `-f` formats floats in the laptop's locale, which uses a decimal comma, and the
  receiver parses with the invariant culture, so every line was rejected. The receiver was right to be strict.
  Fixed the sender with `[string]::Format(InvariantCulture, ...)`. Watch for the same on Android:
  Kotlin's `String.format` uses the default locale too, so pass `Locale.US` or the phone will send commas.
- CLAUDE.md said ".NET 8 SDK". The laptop only has the .NET 11 RC SDK installed, with .NET 8 runtimes next
  to it. The 11 SDK builds a net8.0 target fine, so it didn't matter, but `dotnet --version` alone would have
  said "wrong SDK".

- Docs (and CLAUDE.md, written before the download) said the SDK was an `.aar`. It is two plain jars:
  `spenremote-v1.0.1.jar` (dated 2019, 25 classes, the whole API) and `sdk-v1.0.0.jar` (2013, three classes,
  Samsung's vendor check). No resources, no manifest, so Gradle takes them as a `fileTree("libs")` dependency.
  API surface from `javap`: `SpenRemote.getInstance().connect(ctx, cb)` hands back a `SpenUnitManager`;
  `getUnit(SpenUnit.TYPE_AIR_MOTION)` / `TYPE_BUTTON`, then `registerSpenEventListener(listener, unit)`.
  Events arrive as `SpenEvent`; wrap with `AirMotionEvent(e).getDeltaX()/getDeltaY()` or `ButtonEvent(e).getAction()`.

Android forwarder written and building (`android/app/src/main/java/com/digidan/spenmouse/MainActivity.kt`, 92 lines).
Not yet run on the phone. Android Studio "Rabbit 2" generated the project with AGP 9.5 / compileSdk 37; AGP 9 has
Kotlin built in, so there is no separate kotlin-android plugin in the build file.

- Things the SDK getting-started page doesn't tell you, found with `javap -c` on `SpenRemote`:
  the manifest needs `<uses-permission android:name="com.samsung.android.sdk.penremote.BIND_SPEN_REMOTE"/>`
  (the SDK logs "Add ... BIND_SPEN_REMOTE permission" and fails to bind without it), `connect()` insists on an
  Activity context, and it binds to `com.samsung.android.service.aircommand`, so on Android 11+ the app also needs
  a `<queries><package .../></queries>` entry or the bind silently fails. Failure codes: `SpenRemote.Error.UNSUPPORTED_DEVICE`,
  `CONNECTION_FAILED`, `UNKNOWN`.
- "It was sure and wrong" #2 (mine): dropped the Material library to keep the dependency list short, forgot the
  generated theme inherits from `Theme.Material3`. First Gradle build took 8m44s to download everything and then
  failed at resource linking. Theme now inherits from `Theme.AppCompat.DayNight.NoActionBar`.
- Sign of `deltaY` is unverified. If the cursor moves the wrong way vertically, flip it on the laptop side.
- The Activity sets `FLAG_KEEP_SCREEN_ON`: when the phone screen sleeps the Activity pauses and the listeners
  are unregistered, which would look like the pen "randomly stopping".
