package com.themrrage.moodscape.mirror;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.net.wifi.WifiManager;
import android.os.SystemClock;
import android.os.Bundle;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.PermissionRequest;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import java.io.InputStream;
import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetSocketAddress;
import java.net.SocketTimeoutException;
import java.nio.charset.StandardCharsets;
import java.util.regex.Pattern;

/**
 * Moodscape Mirror for Android: the Mirror page (bundled in the app) in a full-screen WebView.
 *
 * The page is served from https://appassets.androidplatform.net/ so it counts as a secure page, which the camera
 * needs. The painting itself is made on the laptop running the PC painter; the page connects to it over the
 * address the user scans or pastes (or a moodscape://pair?pc=... link).
 *
 * It also listens on the local Wi-Fi for the painter's announcement (a small UDP broadcast on port 47823 with the
 * laptop's local address and its current public link), so after the first time it finds the laptop by itself,
 * even though the public link changes every time the painter restarts. The page asks for it through
 * window.MoodscapeNative.beacon().
 */
public class MainActivity extends Activity {
    static final String HOST = "appassets.androidplatform.net";
    static final Pattern SAFE_PC = Pattern.compile("^https?://[A-Za-z0-9.-]+(:\\d+)?/?$");
    static final int BEACON_PORT = 47823;
    WebView web;
    PermissionRequest pending;
    volatile String beaconMsg = "", beaconFrom = "";
    volatile long beaconAt = 0;
    volatile boolean listening = false;
    Thread listener;
    WifiManager.MulticastLock wifiLock;

    /** What the page can ask the app: the last announcement heard from a painter on this Wi-Fi. */
    public class Bridge {
        @JavascriptInterface
        public String beacon() {
            if (beaconAt == 0) return "";
            long age = SystemClock.elapsedRealtime() - beaconAt;
            return "{\"msg\":" + beaconMsg + ",\"from\":\"" + beaconFrom + "\",\"age\":" + age + "}";
        }

        @JavascriptInterface
        public String version() { return "1.4"; }
    }

    void startListening() {
        if (listening) return;
        listening = true;
        try {
            WifiManager wm = (WifiManager) getApplicationContext().getSystemService(WIFI_SERVICE);
            if (wm != null) { wifiLock = wm.createMulticastLock("moodscape-beacon"); wifiLock.setReferenceCounted(false); wifiLock.acquire(); }
        } catch (Exception ignored) { }   // some phones drop broadcasts without this lock; without it we may hear nothing
        listener = new Thread(() -> {
            byte[] buf = new byte[1500];
            while (listening) {
                try (DatagramSocket sock = new DatagramSocket(null)) {
                    sock.setReuseAddress(true);
                    sock.setBroadcast(true);
                    sock.bind(new InetSocketAddress(BEACON_PORT));
                    sock.setSoTimeout(1000);
                    while (listening) {
                        DatagramPacket pk = new DatagramPacket(buf, buf.length);
                        try { sock.receive(pk); } catch (SocketTimeoutException t) { continue; }
                        String msg = new String(pk.getData(), 0, pk.getLength(), StandardCharsets.UTF_8);
                        // only a painter's announcement: a small JSON object starting with its name; the page parses it
                        if (pk.getLength() < 1200 && msg.startsWith("{\"app\":\"moodscape-mirror\"") && msg.endsWith("}")
                                && pk.getAddress() != null && pk.getAddress().getAddress().length == 4) {
                            beaconMsg = msg;
                            beaconFrom = pk.getAddress().getHostAddress();
                            beaconAt = SystemClock.elapsedRealtime();
                        }
                    }
                } catch (Exception e) {
                    try { Thread.sleep(3000); } catch (InterruptedException ie) { return; }
                }
            }
        }, "beacon");
        listener.setDaemon(true);
        listener.start();
    }

    void stopListening() {
        listening = false;
        if (listener != null) listener.interrupt();
        listener = null;
        try { if (wifiLock != null && wifiLock.isHeld()) wifiLock.release(); } catch (Exception ignored) { }
    }

    @Override
    protected void onCreate(Bundle state) {
        super.onCreate(state);
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        WebView.setWebContentsDebuggingEnabled(false);
        web = new WebView(this);
        web.setBackgroundColor(0xFF07080C);
        setContentView(web);
        hideBars();

        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setMediaPlaybackRequiresUserGesture(false);
        s.setAllowFileAccess(false);
        s.setAllowContentAccess(false);
        // the painter on this Wi-Fi is plain http://192.168.x.x:8765 (only the public link has https)
        s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        web.addJavascriptInterface(new Bridge(), "MoodscapeNative");

        web.setWebViewClient(new WebViewClient() {
            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest req) {
                Uri u = req.getUrl();
                if (!HOST.equals(u.getHost())) return null;            // everything else goes to the network as usual
                String path = u.getPath();
                String name = (path == null || path.equals("/")) ? "mirror.html" : path.substring(1);
                if (name.contains("..")) return notFound();
                try {
                    InputStream in = getAssets().open(name);
                    return new WebResourceResponse(mime(name), "utf-8", in);
                } catch (Exception e) {
                    return notFound();
                }
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest req) {
                Uri u = req.getUrl();
                if (HOST.equals(u.getHost())) return false;
                try { startActivity(new Intent(Intent.ACTION_VIEW, u)); } catch (Exception ignored) { }
                return true;                                            // other links (a kept loop) open in the browser
            }
        });

        web.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onPermissionRequest(final PermissionRequest req) {
                runOnUiThread(() -> {
                    if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
                        grant(req);
                    } else {
                        pending = req;
                        requestPermissions(new String[]{Manifest.permission.CAMERA}, 1);
                    }
                });
            }
        });

        web.loadUrl("https://" + HOST + "/mirror.html" + pairFragment(getIntent()));
    }

    static String mime(String name) {
        if (name.endsWith(".html")) return "text/html";
        if (name.endsWith(".png")) return "image/png";
        if (name.endsWith(".js") || name.endsWith(".mjs")) return "text/javascript";
        if (name.endsWith(".json")) return "application/json";
        if (name.endsWith(".css")) return "text/css";
        return "application/octet-stream";
    }

    static WebResourceResponse notFound() {
        return new WebResourceResponse("text/plain", "utf-8", 404, "Not found", null, null);
    }

    /** moodscape://pair?pc=https://abc.trycloudflare.com  ->  #pc=https://abc.trycloudflare.com */
    static String pairFragment(Intent intent) {
        Uri data = intent == null ? null : intent.getData();
        if (data == null || !"moodscape".equals(data.getScheme())) return "";
        String pc = data.getQueryParameter("pc");
        return (pc != null && SAFE_PC.matcher(pc).matches()) ? "#pc=" + pc : "";
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        String frag = pairFragment(intent);
        if (!frag.isEmpty()) web.loadUrl("https://" + HOST + "/mirror.html" + frag);
    }

    void grant(PermissionRequest req) {
        for (String r : req.getResources()) {
            if (PermissionRequest.RESOURCE_VIDEO_CAPTURE.equals(r)) {
                req.grant(new String[]{PermissionRequest.RESOURCE_VIDEO_CAPTURE});
                return;
            }
        }
        req.deny();
    }

    @Override
    public void onRequestPermissionsResult(int code, String[] perms, int[] results) {
        if (pending == null) return;
        if (results.length > 0 && results[0] == PackageManager.PERMISSION_GRANTED) grant(pending); else pending.deny();
        pending = null;
    }

    @SuppressWarnings("deprecation")
    void hideBars() {
        getWindow().getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                | View.SYSTEM_UI_FLAG_FULLSCREEN | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION);
    }

    @Override
    public void onWindowFocusChanged(boolean focus) {
        super.onWindowFocusChanged(focus);
        if (focus) hideBars();
    }

    @Override
    protected void onResume() { super.onResume(); web.onResume(); startListening(); }

    @Override
    protected void onPause() { stopListening(); web.onPause(); super.onPause(); }

    @SuppressWarnings("deprecation")
    @Override
    public void onBackPressed() {
        if (web.canGoBack()) web.goBack(); else super.onBackPressed();
    }
}
