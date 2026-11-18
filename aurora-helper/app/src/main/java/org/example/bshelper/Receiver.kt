package org.example.bshelper
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import com.aurora.gplayapi.PlayClient
import com.aurora.gplayapi.web.WebPlayClient
import com.aurora.gplayapi.model.ContentType
import com.aurora.gplayapi.android.NativeDeviceProperties
import com.aurora.gplayapi.device.DeviceRegistry
import com.aurora.gplayapi.engine.OkHttpEngine
import com.aurora.gplayapi.model.AppFile
import com.aurora.gplayapi.session.DefaultPlaySessions
import kotlinx.coroutines.runBlocking
import okhttp3.OkHttpClient
import okhttp3.Request
import org.json.JSONObject
import org.json.JSONArray
import java.io.File
import java.security.MessageDigest
import java.util.Locale
import android.util.Base64

class Receiver : BroadcastReceiver() {
 override fun onReceive(context: Context, intent: Intent) {
  val pending = goAsync()
  Thread {
   val root=context.getExternalFilesDir(null)!!
   val status=File(root,"status.json")
   fun publish(value: JSONObject) { val tmp=File(root,"status.tmp");tmp.writeText(value.toString());check(tmp.renameTo(status)) { "Could not publish progress" } }
   val download=intent.getStringExtra("operation")=="download"
   try {
    val operation=intent.getStringExtra("operation") ?: "check"
    if(operation=="search" || operation=="categories" || operation=="category") {
      val web=WebPlayClient(OkHttpEngine(OkHttpClient()),locale=Locale.US)
      val entries=JSONArray()
      if(operation=="categories") { runBlocking { web.categories.all(ContentType.APPLICATION) }.forEach { entries.put(JSONObject().put("name",it.title).put("url",it.browseUrl)) } }
      else {
        val query=intent.getStringExtra("query") ?: ""
        require(query.length in 1..120)
        val bundle=runBlocking { if(operation=="search") web.search.results(query) else web.categories.stream(query) }
        bundle.clusters.flatMap{it.apps}.distinctBy{it.packageName}.take(25).forEach { entries.put(JSONObject().put("package",it.packageName).put("name",it.displayName).put("developer",it.developerName).put("price",it.price)) }
      }
      publish(JSONObject().put("state","done").put("items",entries));pending.resultCode=1;return@Thread
    }
    val pkg=intent.getStringExtra("package") ?: throw IllegalArgumentException("Missing package")
    require(pkg.matches(Regex("[A-Za-z][A-Za-z0-9_]*(\\.[A-Za-z0-9_]+)+")))
    if(download){publish(JSONObject().put("state","working").put("message","Preparing download"));pending.resultCode=1;pending.finish()}
    val http=OkHttpClient.Builder().callTimeout(5,java.util.concurrent.TimeUnit.MINUTES).build()
    val engine=OkHttpEngine(http)
    val device=DeviceRegistry.parse("native",NativeDeviceProperties.read(context))
    val client=PlayClient(engine,DefaultPlaySessions(engine,device=device,locale=Locale.US))
    val app=runBlocking { client.apps.details(pkg) }
    val json=JSONObject().put("package",app.packageName).put("version",app.versionName).put("code",app.versionCode).put("offer",app.offerType)
    if(download) {
      val cert=try { val sig=context.packageManager.getPackageInfo(pkg,PackageManager.GET_SIGNING_CERTIFICATES).signingInfo!!.apkContentsSigners.single().toByteArray(); Base64.encodeToString(MessageDigest.getInstance("SHA-1").digest(sig),Base64.URL_SAFE or Base64.NO_WRAP or Base64.NO_PADDING) } catch(e:PackageManager.NameNotFoundException) { null }
      require(app.isFree) { "Paid apps cannot be installed anonymously" }
      val delivery=runBlocking { client.delivery.resolveDelivery(pkg,app.versionCode,app.offerType,certificateHash=cert) }
      require(delivery.encryption==null) { "Encrypted delivery is not supported" }
      require(delivery.files.isNotEmpty() && delivery.files.all{it.type==AppFile.Type.BASE || it.type==AppFile.Type.SPLIT}) { "Expansion files are not supported" }
      val dir=File(root,"download");dir.mkdirs();dir.listFiles()?.forEach{it.delete()}
      val paths=JSONArray(); var completed=0L; val total=delivery.files.sumOf { it.size.coerceAtLeast(0) }; var lastReport=0L; val cookie=delivery.downloadAuthCookies.joinToString("; "){it.name+"="+it.value}
      delivery.files.forEachIndexed { i,f ->
        require(f.url.startsWith("https://")); val request=Request.Builder().url(f.url).apply { if(cookie.isNotEmpty()) header("Cookie",cookie) }.build()
        http.newCall(request).execute().use { response ->
          require(response.isSuccessful) { "Download failed: HTTP ${response.code}" }
          val dest=File(dir,"part$i.apk"); val hash=MessageDigest.getInstance("SHA-256")
          var size=0L; response.body!!.byteStream().use { input -> dest.outputStream().use { output -> val buf=ByteArray(65536); while(true){ val n=input.read(buf);if(n<0)break;size+=n;require(size<1000000000);hash.update(buf,0,n);output.write(buf,0,n); val now=System.currentTimeMillis();if(now-lastReport>=1500){lastReport=now;publish(JSONObject().put("state","working").put("completed",completed+size).put("total",total).put("part",i+1).put("parts",delivery.files.size))} } };completed+=size }
          val actual=hash.digest().joinToString(""){"%02x".format(it)}
          require(f.sha256.isNotEmpty() && actual.equals(f.sha256,true)) { "Downloaded file checksum mismatch" }
          paths.put(dest.absolutePath)
        }
      }
      publish(json.put("state","done").put("files",paths))
    } else { pending.resultData=json.toString();pending.resultCode=1 }
   } catch(e:Exception) { if(download || intent.getStringExtra("operation") in listOf("search","categories","category")) publish(JSONObject().put("state","error").put("error",e.javaClass.simpleName+": "+e.message)) else {pending.resultCode=0;pending.resultData=(e.javaClass.simpleName+": "+e.message).take(900)} }
   finally { if(!download)pending.finish() }
  }.start()
 }
}
