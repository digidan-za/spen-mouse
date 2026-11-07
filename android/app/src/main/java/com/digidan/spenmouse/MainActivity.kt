package com.digidan.spenmouse

import android.content.pm.PackageManager
import android.net.wifi.WifiManager
import android.os.Bundle
import android.os.Handler
import android.os.Looper
import android.util.Log
import android.view.WindowManager
import android.widget.ArrayAdapter
import android.widget.EditText
import android.widget.ListView
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
import java.net.Inet4Address
import java.net.InetAddress
import java.net.InetSocketAddress
import java.net.NetworkInterface
import java.nio.ByteBuffer
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
    @Volatile private var lastEvent = 0L
    @Volatile private var lastMotion = 0L
    private val ui = Handler(Looper.getMainLooper())
    private val IdleMs = 8000L   // after this long with the button up, stop the gyro stream (re-arming costs ~1 s)
    private var motionOn = false
    private val motionOff = Runnable { motion(false) }
    private val ticker = object : Runnable {   // honest status: the SDK binding stays "connected" even when the pen's radio has gone to sleep
        override fun run() {
            val now = System.currentTimeMillis()
            if (lastEvent > 0) {
                val age = (now - lastEvent) / 1000
                val hint = "Pen gone quiet? Press its button once. Still dead: dock it and pull it out again."
                status.text = "$sent packets sent. Last pen event ${age}s ago." + (if (age > 5) " $hint" else "")
            }
            if (laptops.values.removeAll { now - it.second > 5000 }) showLaptops()   // beacon stopped: laptop went away
            ui.postDelayed(this, 1000)
        }
    }

    // Discovery: the receiver shouts "SPENMOUSE <port> <name>" to the LAN once a second on 51517. ip -> (name, last heard)
    private val laptops = linkedMapOf<String, Pair<String, Long>>()
    private lateinit var ip: EditText
    private lateinit var found: TextView
    private lateinit var listAdapter: ArrayAdapter<String>
    private var discovery: DatagramSocket? = null
    private var multicast: WifiManager.MulticastLock? = null

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(R.layout.activity_main)
        window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)   // screen off = onPause = pen released
        status = findViewById(R.id.status); found = findViewById(R.id.found); ip = findViewById(R.id.ip)
        val prefs = getPreferences(MODE_PRIVATE)
        ip.setText(prefs.getString("ip", ""))
        setTarget(ip.text.toString())
        ip.doAfterTextChanged { prefs.edit().putString("ip", it.toString()).apply(); setTarget(it.toString()) }
        listAdapter = ArrayAdapter(this, android.R.layout.simple_list_item_1)
        findViewById<ListView>(R.id.laptops).let { l ->
            l.adapter = listAdapter
            l.setOnItemClickListener { _, _, i, _ -> ip.setText(laptops.keys.elementAt(i)) }
        }
        // Android 16+ (local network protection): talking to a LAN address needs this runtime permission.
        if (checkSelfPermission(LOCAL_NET) != PackageManager.PERMISSION_GRANTED) requestPermissions(arrayOf(LOCAL_NET), 1)
    }

    private fun listenForLaptops() = Thread {
        val sock = DatagramSocket(null).apply { reuseAddress = true; broadcast = true; soTimeout = 1000; bind(InetSocketAddress(51517)) }
        discovery = sock
        val buf = ByteArray(256)
        while (!sock.isClosed) {
            val p = DatagramPacket(buf, buf.size)
            try { sock.receive(p) } catch (e: Exception) { continue }   // timeout, or closed from onPause
            val parts = String(p.data, 0, p.length).trim().split(" ", limit = 3)
            val from = p.address.hostAddress ?: continue
            if (parts.size == 3 && parts[0] == "SPENMOUSE") ui.post {
                val new = from !in laptops
                laptops[from] = parts[2] to System.currentTimeMillis()
                if (new) showLaptops()
                // Only one laptop on this Wi-Fi and the saved address is not it (typically a stale one): just use it.
                if (new && laptops.size == 1 && ip.text.toString().trim() !in laptops) ip.setText(from)
            }
        }
    }.apply { isDaemon = true; start() }

    // Some Wi-Fi (hotspots, office networks) drops broadcast. So also ask: "SPENMOUSE?" to the broadcast addresses and,
    // on a subnet of 256 hosts or fewer, to every host directly. The receiver answers with its beacon line.
    private val prober = object : Runnable {
        override fun run() {
            discovery?.let { sock -> net.execute {
                val msg = "SPENMOUSE?".toByteArray()
                for (t in probeTargets()) runCatching { sock.send(DatagramPacket(msg, msg.size, t, port)) }
            } }
            ui.postDelayed(this, 4000)
        }
    }

    private fun probeTargets(): List<InetAddress> = runCatching {
        val out = mutableListOf(InetAddress.getByName("255.255.255.255"))
        for (nic in NetworkInterface.getNetworkInterfaces()) for (ia in nic.interfaceAddresses) {
            val a = ia.address as? Inet4Address ?: continue
            if (a.isLoopbackAddress) continue
            ia.broadcast?.let { out += it }
            val bits = 32 - ia.networkPrefixLength
            if (bits in 1..8) {
                val base = ByteBuffer.wrap(a.address).int and (-1 shl bits)
                for (h in 1 until (1 shl bits) - 1) out += InetAddress.getByAddress(ByteBuffer.allocate(4).putInt(base or h).array())
            }
        }
        out
    }.getOrDefault(emptyList())

    private fun showLaptops() {
        listAdapter.clear()
        listAdapter.addAll(laptops.map { "${it.value.first}   ${it.key}" })
        found.text = if (laptops.isEmpty()) "Looking for a laptop running spen-mouse on this Wi-Fi…" else "Tap your laptop:"
    }

    override fun onResume() {
        super.onResume()
        ui.post(ticker)
        showLaptops()
        multicast = (applicationContext.getSystemService(WIFI_SERVICE) as WifiManager).createMulticastLock("spen-mouse").also { it.acquire() }
        listenForLaptops(); ui.post(prober)
        val remote = SpenRemote.getInstance()
        if (!remote.isFeatureEnabled(SpenRemote.FEATURE_TYPE_AIR_MOTION)) {
            show("This phone's S Pen has no air motion"); return
        }
        remote.connect(this, object : SpenRemote.ConnectionResultCallback {
            override fun onSuccess(manager: SpenUnitManager) {
                units = manager
                manager.registerSpenEventListener(SpenEventListener { e ->
                    val down = ButtonEvent(e).action == ButtonEvent.ACTION_DOWN
                    lastEvent = System.currentTimeMillis(); Log.d("spen-mouse", if (down) "button down" else "button up")
                    ui.removeCallbacks(motionOff)
                    // Docking the pen resets its mode under us; a stale registration then delivers nothing. Re-arm if quiet.
                    if (down) ui.post { if (motionOn && System.currentTimeMillis() - lastMotion > IdleMs) motion(false); motion(true) }
                    else ui.postDelayed(motionOff, IdleMs)
                    send(if (down) "B down" else "B up")
                }, manager.getUnit(SpenUnit.TYPE_BUTTON))
                motion(true); ui.postDelayed(motionOff, IdleMs)
                show("Pen connected. Hold the button and wave; tap it to click.")
            }
            override fun onFailure(code: Int) = show("Pen connect failed, code $code")
        })
    }

    // Registering for air motion puts the pen's gyro in "always on" mode (Samsung logs it as SENSOR_ON) and the pen
    // hangs up after a minute or two, drained. So only listen while the button is in use, plus IdleMs of grace.
    private fun motion(on: Boolean) {
        val m = units ?: return
        if (on == motionOn) return
        motionOn = on
        if (on) m.registerSpenEventListener(SpenEventListener { e ->
            val a = AirMotionEvent(e); lastEvent = System.currentTimeMillis(); lastMotion = lastEvent
            send(String.format(Locale.US, "M %.4f %.4f", a.deltaX, a.deltaY))   // Locale.US: no decimal commas
        }, m.getUnit(SpenUnit.TYPE_AIR_MOTION))
        else m.unregisterSpenEventListener(m.getUnit(SpenUnit.TYPE_AIR_MOTION))
        Log.d("spen-mouse", "air motion ${if (on) "on" else "off"}")
    }

    override fun onPause() {
        super.onPause()
        ui.removeCallbacks(ticker); ui.removeCallbacks(motionOff); ui.removeCallbacks(prober)
        discovery?.close(); discovery = null
        multicast?.release(); multicast = null
        motion(false)
        units?.unregisterSpenEventListener(units?.getUnit(SpenUnit.TYPE_BUTTON))
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
        sent++
    }

    private fun show(text: String) = runOnUiThread { status.text = text; Log.i("spen-mouse", text) }
}
