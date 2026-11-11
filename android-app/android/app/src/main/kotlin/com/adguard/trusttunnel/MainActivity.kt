package com.adguard.trusttunnel

import android.os.Build
import android.os.Handler
import android.os.Looper
import io.flutter.embedding.android.FlutterActivity
import io.flutter.embedding.engine.FlutterEngine
import io.flutter.plugin.common.MethodChannel
import java.util.concurrent.Executors

class MainActivity : FlutterActivity() {
    private val worker = Executors.newSingleThreadExecutor()
    private val main = Handler(Looper.getMainLooper())

    override fun configureFlutterEngine(flutterEngine: FlutterEngine) {
        super.configureFlutterEngine(flutterEngine)
        // SABI TUNNEL self-update (lib/sabi/sabi_sync.dart)
        MethodChannel(flutterEngine.dartExecutor.binaryMessenger, "sabi/update").setMethodCallHandler { call, result ->
            when (call.method) {
                "abis" -> result.success(Build.SUPPORTED_ABIS.toList())
                "download" -> {
                    val urls = call.argument<List<String>>("urls") ?: emptyList()
                    val sha = call.argument<String>("sha256") ?: ""
                    val size = call.argument<Number>("size")?.toLong() ?: 0L
                    val name = call.argument<String>("name") ?: "update.apk"
                    val ua = call.argument<String>("ua") ?: "SabiTunnel-Android"
                    worker.execute {
                        try {
                            val path = SabiUpdate.download(applicationContext, urls, sha, size, name, ua)
                            main.post { result.success(path) }
                        } catch (e: Exception) {
                            main.post { result.error("download", e.message ?: e.toString(), null) }
                        }
                    }
                }
                "install" -> {
                    val path = call.argument<String>("path") ?: ""
                    val quiet = call.argument<Boolean>("quiet") ?: false
                    worker.execute {
                        try {
                            SabiUpdate.install(applicationContext, path, quiet)
                            main.post { result.success(null) }
                        } catch (e: Exception) {
                            main.post { result.error("install", e.message ?: e.toString(), null) }
                        }
                    }
                }
                else -> result.notImplemented()
            }
        }
    }

    override fun onResume() {
        super.onResume()
        SabiUpdate.resumed = true
    }

    override fun onPause() {
        SabiUpdate.resumed = false
        super.onPause()
    }
}
