package com.adguard.trusttunnel

import android.app.PendingIntent
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageInstaller
import android.os.Build
import java.io.File
import java.io.FileInputStream
import java.net.HttpURLConnection
import java.net.URL
import java.security.MessageDigest

/** SABI TUNNEL self-update: downloads the APK the panel feed names, checks size and SHA-256, installs it. */
object SabiUpdate {
    @Volatile
    var resumed = false

    private fun dir(ctx: Context): File = File(ctx.cacheDir, "sabi-update").apply { mkdirs() }

    private fun hex(b: ByteArray): String = b.joinToString("") { "%02x".format(it) }

    private fun sha256(f: File): String {
        val md = MessageDigest.getInstance("SHA-256")
        FileInputStream(f).use { s ->
            val buf = ByteArray(1 shl 16)
            while (true) {
                val n = s.read(buf)
                if (n < 0) break
                md.update(buf, 0, n)
            }
        }
        return hex(md.digest())
    }

    /** Returns the path of the verified APK (kept in the cache, so a second call is quick). */
    fun download(ctx: Context, urls: List<String>, sha: String, size: Long, name: String, ua: String): String {
        require(Regex("[A-Za-z0-9._-]{1,80}").matches(name)) { "bad file name" }
        require(sha.length == 64) { "bad sha256" }
        val d = dir(ctx)
        val dest = File(d, name)
        if (dest.isFile && (size <= 0 || dest.length() == size) && sha256(dest).equals(sha, ignoreCase = true)) return dest.path
        d.listFiles()?.forEach { if (it.name != name) it.delete() }
        var last = "no download address"
        for (u in urls) {
            if (!u.startsWith("https://")) continue
            val part = File(d, "$name.part")
            var conn: HttpURLConnection? = null
            try {
                val c = URL(u).openConnection() as HttpURLConnection
                conn = c
                c.connectTimeout = 15000
                c.readTimeout = 30000
                c.setRequestProperty("User-Agent", ua)
                val code = c.responseCode
                if (code != 200) {
                    last = "HTTP $code"
                    continue
                }
                val md = MessageDigest.getInstance("SHA-256")
                var total = 0L
                c.inputStream.use { inp ->
                    part.outputStream().use { out ->
                        val buf = ByteArray(1 shl 16)
                        while (true) {
                            val n = inp.read(buf)
                            if (n < 0) break
                            total += n
                            if (size > 0 && total > size) throw IllegalStateException("larger than announced")
                            md.update(buf, 0, n)
                            out.write(buf, 0, n)
                        }
                    }
                }
                if (size > 0 && total != size) {
                    last = "got $total bytes, expected $size"
                    part.delete()
                    continue
                }
                if (!hex(md.digest()).equals(sha, ignoreCase = true)) {
                    last = "SHA-256 does not match"
                    part.delete()
                    continue
                }
                dest.delete()
                if (!part.renameTo(dest)) throw IllegalStateException("cannot store the download")
                return dest.path
            } catch (e: Exception) {
                last = e.message ?: e.toString()
                part.delete()
            } finally {
                conn?.disconnect()
            }
        }
        throw IllegalStateException(last)
    }

    /** quiet: no question asked (Android 12+ lets an app update itself silently); otherwise Android asks. */
    fun install(ctx: Context, path: String, quiet: Boolean) {
        val f = File(path)
        require(f.isFile && f.parentFile?.canonicalPath == dir(ctx).canonicalPath) { "nothing to install" }
        if (quiet && Build.VERSION.SDK_INT < 31) return
        val installer = ctx.packageManager.packageInstaller
        val params = PackageInstaller.SessionParams(PackageInstaller.SessionParams.MODE_FULL_INSTALL)
        params.setAppPackageName(ctx.packageName)
        params.setSize(f.length())
        if (Build.VERSION.SDK_INT >= 31) {
            params.setRequireUserAction(
                if (quiet) PackageInstaller.SessionParams.USER_ACTION_NOT_REQUIRED
                else PackageInstaller.SessionParams.USER_ACTION_REQUIRED
            )
        }
        val id = installer.createSession(params)
        try {
            installer.openSession(id).use { s ->
                FileInputStream(f).use { inp ->
                    s.openWrite("base.apk", 0, f.length()).use { out ->
                        inp.copyTo(out, 1 shl 16)
                        s.fsync(out)
                    }
                }
                val intent = Intent(ctx, SabiUpdateReceiver::class.java).putExtra("quiet", quiet)
                val flags = PendingIntent.FLAG_UPDATE_CURRENT or
                    (if (Build.VERSION.SDK_INT >= 31) PendingIntent.FLAG_MUTABLE else 0)
                s.commit(PendingIntent.getBroadcast(ctx, id, intent, flags).intentSender)
            }
        } catch (e: Exception) {
            try {
                installer.abandonSession(id)
            } catch (_: Exception) {
            }
            throw e
        }
    }
}

/** Gets the result of an install session; shows Android's confirmation when one is needed and the app is on screen. */
class SabiUpdateReceiver : BroadcastReceiver() {
    @Suppress("DEPRECATION")
    override fun onReceive(ctx: Context, intent: Intent) {
        val status = intent.getIntExtra(PackageInstaller.EXTRA_STATUS, PackageInstaller.STATUS_FAILURE)
        if (status != PackageInstaller.STATUS_PENDING_USER_ACTION) return
        val confirm: Intent? = if (Build.VERSION.SDK_INT >= 33) {
            intent.getParcelableExtra(Intent.EXTRA_INTENT, Intent::class.java)
        } else {
            intent.getParcelableExtra(Intent.EXTRA_INTENT)
        }
        val quiet = intent.getBooleanExtra("quiet", false)
        if (confirm == null || (quiet && !SabiUpdate.resumed)) {
            // a background attempt that needs the user after all: drop it, the app asks while it is on screen
            val id = intent.getIntExtra(PackageInstaller.EXTRA_SESSION_ID, -1)
            if (id >= 0) {
                try {
                    ctx.packageManager.packageInstaller.abandonSession(id)
                } catch (_: Exception) {
                }
            }
            return
        }
        confirm.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK)
        try {
            ctx.startActivity(confirm)
        } catch (_: Exception) {
        }
    }
}
