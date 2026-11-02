package com.brenpoly.cardboardxr.windows;

import android.content.AttributionSource;
import android.content.Context;
import android.content.ContextWrapper;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.os.Binder;
import android.os.Parcel;
import android.os.Process;
import android.util.Log;
import android.view.Surface;

import java.io.IOException;
import java.lang.reflect.Constructor;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

// Runs as the shell user, started by Shizuku in its own process. Only shell may make a trusted virtual
// display, the kind other apps can be launched onto, and only shell may inject input into them. The
// display draws into a Surface the app hands over (AppWindows), so the app gets the picture without
// any more privilege than that. Input goes through the input command, which starts a process for
// each event: enough for taps, too slow for anything continuous.
public class WindowService extends Binder {
    static final int CREATE = FIRST_CALL_TRANSACTION;
    static final int LAUNCH = FIRST_CALL_TRANSACTION + 1;
    static final int TAP = FIRST_CALL_TRANSACTION + 2;
    static final int SWIPE = FIRST_CALL_TRANSACTION + 3;
    static final int KEY = FIRST_CALL_TRANSACTION + 4;
    // Shizuku sends this to a user service it's unbinding with remove set.
    static final int DESTROY = 16777115;

    private static final String TAG = "CardboardXRApps";
    private static final String SHELL_PACKAGE = "com.android.shell";

    // DisplayManager.VIRTUAL_DISPLAY_FLAG_*; all but the first three are hidden.
    private static final int FLAGS = 1 // PUBLIC
            | 1 << 3 // OWN_CONTENT_ONLY: never mirrors the phone's own screen
            | 1 << 6 // SUPPORTS_TOUCH
            | 1 << 8 // DESTROY_CONTENT_ON_REMOVAL: apps close with the window, not jump to the phone's screen
            | 1 << 10 // TRUSTED
            | 1 << 12 // ALWAYS_UNLOCKED
            | 1 << 13 // TOUCH_FEEDBACK_DISABLED
            | 1 << 14; // OWN_FOCUS, though on a Galaxy S23 FE an app opening here takes focus anyway: see returnFocus

    // Not OWN_DISPLAY_GROUP: a display in its own group stays off, and the app on it draws only black.

    private final Context mContext;
    private final ExecutorService mCommands = Executors.newSingleThreadExecutor();
    private VirtualDisplay mDisplay;
    private String mHomeActivity;

    // DisplayManager checks that the package it's told about belongs to the caller, and that's shell.
    private static class ShellContext extends ContextWrapper {
        ShellContext(Context base) {
            super(base);
        }

        @Override
        public String getPackageName() {
            return SHELL_PACKAGE;
        }

        @Override
        public String getOpPackageName() {
            return SHELL_PACKAGE;
        }

        @Override
        public AttributionSource getAttributionSource() {
            return new AttributionSource.Builder(Process.SHELL_UID).setPackageName(SHELL_PACKAGE).build();
        }

        @Override
        public Context getApplicationContext() {
            return this;
        }
    }

    public WindowService(Context context) {
        mContext = new ShellContext(context);
    }

    @Override
    protected boolean onTransact(int code, Parcel data, Parcel reply, int flags) {
        switch (code) {
            case CREATE: {
                Surface surface = Surface.CREATOR.createFromParcel(data);
                int width = data.readInt();
                int height = data.readInt();
                int dpi = data.readInt();
                mHomeActivity = data.readString();
                reply.writeInt(create(surface, width, height, dpi));
                return true;
            }
            case LAUNCH:
                launch(data.readString());
                return true;
            case TAP:
                input("tap", String.valueOf(data.readInt()), String.valueOf(data.readInt()));
                return true;
            case SWIPE:
                input("swipe", String.valueOf(data.readInt()), String.valueOf(data.readInt()),
                        String.valueOf(data.readInt()), String.valueOf(data.readInt()), String.valueOf(data.readInt()));
                return true;
            case KEY:
                input("keyevent", String.valueOf(data.readInt()));
                return true;
            case DESTROY:
                if (mDisplay != null)
                    mDisplay.release();
                System.exit(0);
                return true;
            default:
                return false;
        }
    }

    private int create(Surface surface, int width, int height, int dpi) {
        try {
            Constructor<DisplayManager> constructor = DisplayManager.class.getDeclaredConstructor(Context.class);
            constructor.setAccessible(true);
            DisplayManager manager = constructor.newInstance(mContext);
            mDisplay = manager.createVirtualDisplay("CardboardXR Window", width, height, dpi, surface, FLAGS);
            if (mDisplay == null)
                return -1;
            return mDisplay.getDisplay().getDisplayId();
        } catch (ReflectiveOperationException | RuntimeException e) {
            Log.e(TAG, "Couldn't make the display", e);
            return -1;
        }
    }

    // Takes the app over if it's already open on the phone's screen: a task is on one display at a time.
    private void launch(String packageName) {
        mCommands.execute(() -> {
            String component = run("cmd", "package", "resolve-activity", "--brief", "--user", "0",
                    "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", packageName);
            String[] lines = component.trim().split("\n");
            String result = run("am", "start", "--user", "0", "--display", displayId(),
                    "--activity-clear-task", "-n", lines[lines.length - 1].trim());
            Log.i(TAG, "Launched " + packageName + ": " + result.trim());
            returnFocus();
        });
    }

    // Unity stops drawing without focus, and the app on the display takes it on opening or on a tap.
    // Bringing Unity's activity to the front on the phone's screen gives it back. It's the front
    // already, so this delivers an intent it ignores and starts nothing.
    private void returnFocus() {
        run("am", "start", "--user", "0", "--display", "0", "-f", "0x20000", "-n", mHomeActivity);
    }

    private void input(String... arguments) {
        String[] command = new String[3 + arguments.length];
        command[0] = "input";
        command[1] = "-d";
        command[2] = displayId();
        System.arraycopy(arguments, 0, command, 3, arguments.length);
        mCommands.execute(() -> {
            run(command);
            returnFocus();
        });
    }

    private String displayId() {
        return mDisplay == null ? "-1" : String.valueOf(mDisplay.getDisplay().getDisplayId());
    }

    private static String run(String... command) {
        try {
            java.lang.Process process = new ProcessBuilder(command).redirectErrorStream(true).start();
            String output = new String(process.getInputStream().readAllBytes(), StandardCharsets.UTF_8);
            process.waitFor();
            return output;
        } catch (IOException | InterruptedException e) {
            Log.e(TAG, "Couldn't run " + command[0], e);
            return "";
        }
    }
}
