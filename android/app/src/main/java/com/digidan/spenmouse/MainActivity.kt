package com.digidan.spenmouse

import android.content.pm.PackageManager
import android.os.Bundle
import android.util.Log
import android.view.WindowManager
import android.widget.EditText
import android.widget.TextView
import androidx.appcompat.app.AppCompatActivity
import androidx.core.widget.doAfterTextChanged
import com.samsung.android.sdk.penremote.AirMotionEvent
import com.samsung.android.sdk.penremote.ButtonEvent
import com.samsung.android.sdk.penremote.SpenEventListener
import com.samsung.android.sdk.penremote.SpenRemote
import com.samsung.android.sdk.penremote.SpenUnit
import com.samsung.android.sdk.penremote.SpenUnitManager
import java.net.DatagramPacket
import java.net.DatagramSocket
import java.net.InetAddress
import java.util.Locale
import java.util.concurrent.Executors

/**
 * Forwards S Pen air motion and button events to the laptop as UDP lines:
 * "M dx dy" (floats, -1..1) and "B down" / "B up". See ../windows/Program.cs for the other end.
 */
class MainActivity : AppCompatActivity() {
    private val port = 51516
    private val LOCAL_NET = "android.permission.ACCESS_LOCAL_NETWORK"
    private val net = Executors.newSingleThreadExecutor()   // all socket work happens here
    private val socket by lazy { DatagramSocket() }
    @Volatile private var target: InetAddress? = null
    private var units: SpenUnitManager? = null
    private lateinit var status: TextView
    private var sent = 0

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)   // screen off = onPause = pen released
        status = findViewById(R.id.status)
        val ip = findViewById<EditText>(R.id.ip)
        val prefs = getPreferences(MODE_PRIVATE)
        ip.setText(prefs.getString("ip", ""))
        setTarget(ip.text.toString())
        ip.doAfterTextChanged { prefs.edit().putString("ip", it.toString()).apply(); setTarget(it.toString()) }
        // Android 16+ (local network protection): talking to a LAN address needs this runtime permission.
        if (checkSelfPermission(LOCAL_NET) != PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(LOCAL_NET), 1)
    }

    override fun onResume() {
        super.onResume()
        val remote = SpenRemote.getInstance()
        if (!remote.isFeatureEnabled(SpenRemote.FEATURE_TYPE_AIR_MOTION)) {
            show("This phone's S Pen has no air motion"); return
        }
        remote.connect(this, object : SpenRemote.ConnectionResultCallback {
            override fun onSuccess(manager: SpenUnitManager) {
                units = manager
                manager.registerSpenEventListener(SpenEventListener { e ->
                    val m = AirMotionEvent(e)
                    send(String.format(Locale.US, "M %.4f %.4f", m.deltaX, m.deltaY))   // Locale.US: no decimal commas
                }, manager.getUnit(SpenUnit.TYPE_AIR_MOTION))
                manager.registerSpenEventListener(SpenEventListener { e ->
                    send(if (ButtonEvent(e).action == ButtonEvent.ACTION_DOWN) "B down" else "B up")
                }, manager.getUnit(SpenUnit.TYPE_BUTTON))
                show("Pen connected. Hold the button and wave; tap it to click.")
            }
            override fun onFailure(code: Int) = show("Pen connect failed, code $code")
        })
    }

    override fun onPause() {
        super.onPause()
        units?.let {   // air motion drains the pen's battery, so stop listening when not in front
            it.unregisterSpenEventListener(it.getUnit(SpenUnit.TYPE_AIR_MOTION))
            it.unregisterSpenEventListener(it.getUnit(SpenUnit.TYPE_BUTTON))
        }
        units = null
        SpenRemote.getInstance().let { if (it.isConnected) it.disconnect(this) }
    }

    private fun setTarget(host: String) = net.execute {
        target = runCatching { InetAddress.getByName(host.trim()) }.getOrNull()
    }

    private fun send(line: String) = net.execute {
        val t = target ?: return@execute
        val bytes = line.toByteArray()
        runCatching { socket.send(DatagramPacket(bytes, bytes.size, t, port)) }
            .onFailure { Log.w("spen-mouse", "send failed", it) }
        if (++sent % 100 == 0) show("Pen connected. $sent packets sent to ${t.hostAddress}")
    }

    private fun show(text: String) = runOnUiThread { status.text = text; Log.i("spen-mouse", text) }
}
