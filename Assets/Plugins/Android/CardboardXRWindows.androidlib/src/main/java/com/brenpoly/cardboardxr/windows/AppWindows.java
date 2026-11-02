package com.brenpoly.cardboardxr.windows;

import android.content.ComponentName;
import android.content.ServiceConnection;
import android.content.pm.PackageManager;
import android.graphics.PixelFormat;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.IBinder;
import android.os.Parcel;
import android.os.RemoteException;
import android.util.Log;

import java.nio.ByteBuffer;

import rikka.shizuku.Shizuku;

// The app's side of a window onto another Android app; AppWindowBridge.cs calls it. Gets the shell
// process running (WindowService) through Shizuku, gives it a Surface to draw a virtual display into,
// and copies each frame into one of two buffers Unity owns. The sequence number says which is
// finished: the buffer for sequence % 2, the other being the one written next. A copy works with
// either graphics API, where a shared GPU texture would need one path each for Vulkan and GLES.
public final class AppWindows {
    public static final int IDLE = 0;
    public static final int WAITING_FOR_PERMISSION = 1;
    public static final int STARTING = 2;
    public static final int RUNNING = 3;
    public static final int NO_SHIZUKU = -1;
    public static final int PERMISSION_DENIED = -2;
    public static final int FAILED = -3;

    private static final String TAG = "CardboardXRApps";
    private static final int PERMISSION_REQUEST = 1;
    private static final String UNITY_ACTIVITY = "com.unity3d.player.UnityPlayerGameActivity";

    private static volatile int sState = IDLE;
    private static volatile long sSequence;
    private static ByteBuffer[] sBuffers;
    private static String sAppPackage;
    private static String sLaunchPackage;
    private static int sWidth;
    private static int sHeight;
    private static int sDpi;

    private static HandlerThread sThread;
    private static ImageReader sReader;
    private static IBinder sService;
    private static Shizuku.UserServiceArgs sArgs;

    private static final Shizuku.OnRequestPermissionResultListener sPermissionListener = (requestCode, result) -> {
        if (requestCode != PERMISSION_REQUEST)
            return;
        removePermissionListener();
        if (result == PackageManager.PERMISSION_GRANTED)
            connect();
        else
            sState = PERMISSION_DENIED;
    };

    private static final ServiceConnection sConnection = new ServiceConnection() {
        @Override
        public void onServiceConnected(ComponentName name, IBinder service) {
            sService = service;
            startDisplay();
        }

        @Override
        public void onServiceDisconnected(ComponentName name) {
            sService = null;
            if (sState != IDLE)
                sState = FAILED;
        }
    };

    private AppWindows() {
    }

    public static int state() {
        return sState;
    }

    public static long sequence() {
        return sSequence;
    }

    // Frames are written as RGBA, rows top first, into the two buffers, width * height * 4 bytes each.
    public static void start(ByteBuffer first, ByteBuffer second, int width, int height, int dpi, String appPackage,
            String launchPackage) {
        sBuffers = new ByteBuffer[] { first, second };
        sWidth = width;
        sHeight = height;
        sDpi = dpi;
        sAppPackage = appPackage;
        sLaunchPackage = launchPackage;
        sSequence = 0;
        try {
            if (!Shizuku.pingBinder()) {
                sState = NO_SHIZUKU;
                return;
            }
            if (Shizuku.checkSelfPermission() == PackageManager.PERMISSION_GRANTED) {
                connect();
                return;
            }
            sState = WAITING_FOR_PERMISSION;
            Shizuku.addRequestPermissionResultListener(sPermissionListener);
            Shizuku.requestPermission(PERMISSION_REQUEST);
        } catch (RuntimeException e) {
            Log.e(TAG, "Couldn't reach Shizuku", e);
            sState = NO_SHIZUKU;
        }
    }

    private static void connect() {
        sState = STARTING;
        sArgs = new Shizuku.UserServiceArgs(new ComponentName(sAppPackage, WindowService.class.getName()))
                .daemon(false)
                .processNameSuffix("windows")
                .tag("windows")
                .version(3);
        try {
            Shizuku.bindUserService(sArgs, sConnection);
        } catch (RuntimeException e) {
            Log.e(TAG, "Couldn't start the service", e);
            sState = FAILED;
        }
    }

    private static void startDisplay() {
        sThread = new HandlerThread("CardboardXR Window");
        sThread.start();
        sReader = ImageReader.newInstance(sWidth, sHeight, PixelFormat.RGBA_8888, 3);
        sReader.setOnImageAvailableListener(AppWindows::onImage, new Handler(sThread.getLooper()));

        Parcel data = Parcel.obtain();
        Parcel reply = Parcel.obtain();
        try {
            sReader.getSurface().writeToParcel(data, 0);
            data.writeInt(sWidth);
            data.writeInt(sHeight);
            data.writeInt(sDpi);
            data.writeString(sAppPackage + "/" + UNITY_ACTIVITY);
            sService.transact(WindowService.CREATE, data, reply, 0);
            int display = reply.readInt();
            Log.i(TAG, "Display " + display);
            if (display < 0) {
                sState = FAILED;
                return;
            }
            sState = RUNNING;
            launch(sLaunchPackage);
        } catch (RemoteException | RuntimeException e) {
            Log.e(TAG, "Couldn't make the display", e);
            sState = FAILED;
        } finally {
            data.recycle();
            reply.recycle();
        }
    }

    private static void onImage(ImageReader reader) {
        Image image = reader.acquireLatestImage();
        if (image == null)
            return;
        try {
            ByteBuffer destination = sBuffers[(int) ((sSequence + 1) % 2)];
            Image.Plane plane = image.getPlanes()[0];
            ByteBuffer source = plane.getBuffer();
            int row = sWidth * 4;
            destination.clear();
            for (int y = 0; y < sHeight; y++) {
                source.limit(y * plane.getRowStride() + row);
                source.position(y * plane.getRowStride());
                destination.put(source);
            }
            sSequence++;
        } finally {
            image.close();
        }
    }

    public static void launch(String packageName) {
        Parcel data = Parcel.obtain();
        data.writeString(packageName);
        send(WindowService.LAUNCH, data);
    }

    public static void tap(int x, int y) {
        Parcel data = Parcel.obtain();
        data.writeInt(x);
        data.writeInt(y);
        send(WindowService.TAP, data);
    }

    public static void swipe(int fromX, int fromY, int toX, int toY, int milliseconds) {
        Parcel data = Parcel.obtain();
        data.writeInt(fromX);
        data.writeInt(fromY);
        data.writeInt(toX);
        data.writeInt(toY);
        data.writeInt(milliseconds);
        send(WindowService.SWIPE, data);
    }

    public static void key(int keyCode) {
        Parcel data = Parcel.obtain();
        data.writeInt(keyCode);
        send(WindowService.KEY, data);
    }

    private static void send(int code, Parcel data) {
        Parcel reply = Parcel.obtain();
        try {
            if (sService != null)
                sService.transact(code, data, reply, 0);
        } catch (RemoteException e) {
            Log.e(TAG, "Lost the service", e);
        } finally {
            data.recycle();
            reply.recycle();
        }
    }

    // Closes the display, and with it the app on it. Unity may free the buffers once this returns.
    public static void stop() {
        sState = IDLE;
        removePermissionListener();
        if (sReader != null)
            sReader.setOnImageAvailableListener(null, null);
        if (sArgs != null && sService != null) {
            try {
                Shizuku.unbindUserService(sArgs, sConnection, true);
            } catch (RuntimeException e) {
                Log.e(TAG, "Couldn't stop the service", e);
            }
        }
        sService = null;
        if (sThread != null) {
            sThread.quitSafely();
            try {
                sThread.join(500);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
            sThread = null;
        }
        if (sReader != null) {
            sReader.close();
            sReader = null;
        }
        sBuffers = null;
    }

    private static void removePermissionListener() {
        try {
            Shizuku.removeRequestPermissionResultListener(sPermissionListener);
        } catch (RuntimeException e) {
            Log.w(TAG, "Couldn't remove the permission listener", e);
        }
    }
}
