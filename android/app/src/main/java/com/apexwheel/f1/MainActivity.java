package com.apexwheel.f1;

import android.app.Activity;
import android.content.Context;
import android.hardware.Sensor;
import android.hardware.SensorEvent;
import android.hardware.SensorEventListener;
import android.hardware.SensorManager;
import android.os.Build;
import android.os.Bundle;
import android.os.VibrationEffect;
import android.os.Vibrator;
import android.view.View;
import android.view.WindowManager;
import android.webkit.JavascriptInterface;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;

import java.net.DatagramPacket;
import java.net.DatagramSocket;
import java.net.InetAddress;

public class MainActivity extends Activity implements SensorEventListener {

    private WebView mWebView;
    private SensorManager mSensorManager;
    private Sensor mRotationSensor;
    private Vibrator mVibrator;

    private String mServerIp = "10.0.2.2"; // Default fallback / USB
    private int mServerPort = 8081;
    private DatagramSocket mUdpSocket;
    private InetAddress mServerAddress;

    private float mSteerAngleDeg = 0f;
    private float mThrottle = 0f;
    private float mBrake = 0f;
    private float mClutch = 0f;
    private int mButtons = 0;
    private long mSeq = 0;

    private final float[] mRotationMatrix = new float[9];
    private final float[] mOrientationAngles = new float[3];

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        // Keep screen awake & fullscreen
        getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        hideSystemUI();

        // Hardware services
        mSensorManager = (SensorManager) getSystemService(Context.SENSOR_SERVICE);
        if (mSensorManager != null) {
            mRotationSensor = mSensorManager.getDefaultSensor(Sensor.TYPE_ROTATION_VECTOR);
            if (mRotationSensor == null) {
                mRotationSensor = mSensorManager.getDefaultSensor(Sensor.TYPE_ACCELEROMETER);
            }
        }
        mVibrator = (Vibrator) getSystemService(Context.VIBRATOR_SERVICE);

        // Init UDP Socket
        new Thread(() -> {
            try {
                mUdpSocket = new DatagramSocket();
                mServerAddress = InetAddress.getByName(mServerIp);
            } catch (Exception e) {
                e.printStackTrace();
            }
        }).start();

        // Setup WebView
        mWebView = new WebView(this);
        setContentView(mWebView);

        WebSettings settings = mWebView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setAllowContentAccess(true);
        settings.setLoadsImagesAutomatically(true);

        mWebView.setWebViewClient(new WebViewClient());
        mWebView.setWebChromeClient(new WebChromeClient());
        mWebView.addJavascriptInterface(new ApexNativeInterface(), "ApexNative");

        // Load bundled F1 Steering Wheel interface
        mWebView.loadUrl("file:///android_asset/index.html");

        // Start High-Frequency UDP Network Worker (100Hz)
        startUdpSenderLoop();
    }

    private void hideSystemUI() {
        View decorView = getWindow().getDecorView();
        decorView.setSystemUiVisibility(
                View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                        | View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                        | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                        | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                        | View.SYSTEM_UI_FLAG_FULLSCREEN
        );
    }

    @Override
    protected void onResume() {
        super.onResume();
        hideSystemUI();
        if (mSensorManager != null && mRotationSensor != null) {
            mSensorManager.registerListener(this, mRotationSensor, SensorManager.SENSOR_DELAY_FASTEST);
        }
    }

    @Override
    protected void onPause() {
        super.onPause();
        if (mSensorManager != null) {
            mSensorManager.unregisterListener(this);
        }
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        if (mUdpSocket != null && !mUdpSocket.isClosed()) {
            mUdpSocket.close();
        }
    }

    @Override
    public void onSensorChanged(SensorEvent event) {
        if (event.sensor.getType() == Sensor.TYPE_ROTATION_VECTOR) {
            SensorManager.getRotationMatrixFromVector(mRotationMatrix, event.values);
            SensorManager.getOrientation(mRotationMatrix, mOrientationAngles);

            // Roll angle when phone held horizontally in landscape
            float rollRad = mOrientationAngles[2];
            mSteerAngleDeg = (float) Math.toDegrees(rollRad);
        }
    }

    @Override
    public void onAccuracyChanged(Sensor sensor, int accuracy) {
    }

    private void startUdpSenderLoop() {
        new Thread(() -> {
            while (!isFinishing()) {
                try {
                    Thread.sleep(10); // ~100Hz
                    if (mUdpSocket == null || mServerAddress == null) continue;

                    mSeq++;
                    String packetStr = "APEX:" + mSeq + ":" +
                            String.format("%.2f", mSteerAngleDeg) + ":" +
                            String.format("%.3f", mThrottle) + ":" +
                            String.format("%.3f", mBrake) + ":" +
                            String.format("%.3f", mClutch) + ":" +
                            mButtons;

                    byte[] data = packetStr.getBytes();
                    DatagramPacket packet = new DatagramPacket(data, data.length, mServerAddress, mServerPort);
                    mUdpSocket.send(packet);
                } catch (Exception e) {
                    // Ignore packet drop
                }
            }
        }).start();
    }

    public class ApexNativeInterface {
        @JavascriptInterface
        public void vibrate(long durationMs) {
            if (mVibrator == null) return;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
                mVibrator.vibrate(VibrationEffect.createOneShot(durationMs, VibrationEffect.DEFAULT_AMPLITUDE));
            } else {
                mVibrator.vibrate(durationMs);
            }
        }

        @JavascriptInterface
        public void setServerIp(String ip) {
            mServerIp = ip;
            new Thread(() -> {
                try {
                    mServerAddress = InetAddress.getByName(mServerIp);
                } catch (Exception e) {
                    e.printStackTrace();
                }
            }).start();
        }

        @JavascriptInterface
        public void onInputUpdate(float throttle, float brake, float clutch, int buttons) {
            mThrottle = throttle;
            mBrake = brake;
            mClutch = clutch;
            mButtons = buttons;
        }

        @JavascriptInterface
        public void onSteerAngle(float angleDeg) {
            mSteerAngleDeg = angleDeg;
        }
    }
}
