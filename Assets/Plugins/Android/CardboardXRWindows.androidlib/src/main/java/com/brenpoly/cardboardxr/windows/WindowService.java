package com.brenpoly.cardboardxr.windows;

import android.content.AttributionSource;
import android.content.Context;
import android.content.ContextWrapper;
import android.hardware.display.DisplayManager;
import android.hardware.display.VirtualDisplay;
import android.os.Binder;
import android.os.Parcel;
import android.os.Process;
import android.os.SystemClock;
import android.util.Log;
import android.view.InputDevice;
import android.view.InputEvent;
import android.view.MotionEvent;
import android.view.Surface;

import java.io.IOException;
import java.lang.reflect.Constructor;
import java.lang.reflect.Method;
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
    static final int TEXT = FIRST_CALL_TRANSACTION + 5;
    static final int OPEN_URL = FIRST_CALL_TRANSACTION + 6;
    static final int SCROLL = FIRST_CALL_TRANSACTION + 7;
    static final int FOCUS = FIRST_CALL_TRANSACTION + 8;
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

    // Android 14's STEAL_TOP_FOCUS_DISABLED: a touch on the display doesn't take the phone's focus
    // from Unity. Without it, a swipe there (Up, Down) stalled the stereo view for about a second, the
    // passthrough frozen then black, until returnFocus gave focus back. Older Android refuses it, and
    // the display is made without it (create).
    private static final int NO_FOCUS_STEAL = 1 << 16;

    // Not OWN_DISPLAY_GROUP: a display in its own group stays off, and the app on it draws only black.

    private final Context mContext;
    private final ExecutorService mCommands = Executors.newSingleThreadExecutor();
    private VirtualDisplay mDisplay;
    private String mHomeActivity;
    // Unity's task, for giving it focus back without restarting its activity (returnFocus).
    private int mHomeTask;
    // The display was made with STEAL_TOP_FOCUS_DISABLED, so nothing on it takes Unity's focus.
    private volatile boolean mFocusSafe;
    // The package launch() found and started: of a list, the first one installed.
    private volatile String mLaunched;

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
                mHomeTask = data.readInt();
                reply.writeInt(create(surface, width, height, dpi));
                return true;
            }
            case LAUNCH:
                launch(data.readString());
                return true;
            case TAP:
                tap(data.readInt(), data.readInt());
                return true;
            case FOCUS:
                mCommands.execute(() -> {
                    // Only focus the window's app took: a screenshot, the Home button or the
                    // notification shade took it on the phone's own screen, and the user meant that.
                    int focused = focusedDisplay();
                    if (mDisplay != null && focused != mDisplay.getDisplay().getDisplayId()) {
                        Log.i(TAG, "Focus is on display " + focused + ", not the window's: leaving it");
                        return;
                    }
                    returnFocus();
                });
                return true;
            case SWIPE:
                input("swipe", String.valueOf(data.readInt()), String.valueOf(data.readInt()),
                        String.valueOf(data.readInt()), String.valueOf(data.readInt()), String.valueOf(data.readInt()));
                return true;
            case KEY:
                input("keyevent", String.valueOf(data.readInt()));
                return true;
            case TEXT:
                // The input command reads %s as a space, and stops at a real one.
                input("text", data.readString().replace(" ", "%s"));
                return true;
            case SCROLL:
                scroll(data.readInt(), data.readInt(), data.readInt(), data.readInt());
                return true;
            case OPEN_URL:
                openUrl(data.readString());
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
            try {
                mDisplay = manager.createVirtualDisplay("CardboardXR Window", width, height, dpi, surface, FLAGS | NO_FOCUS_STEAL);
                mFocusSafe = true;
                Log.i(TAG, "Display made with STEAL_TOP_FOCUS_DISABLED");
            } catch (RuntimeException e) {
                Log.w(TAG, "Display refused STEAL_TOP_FOCUS_DISABLED, making it without: " + e);
                mDisplay = manager.createVirtualDisplay("CardboardXR Window", width, height, dpi, surface, FLAGS);
            }
            if (mDisplay == null)
                return -1;
            int id = mDisplay.getDisplay().getDisplayId();
            // Text is typed through TEXT instead.
            hideIme(id);
            return id;
        } catch (ReflectiveOperationException | RuntimeException e) {
            Log.e(TAG, "Couldn't make the display", e);
            return -1;
        }
    }

    // Takes the app over if it's already open on the phone's screen: a task is on one display at a time.
    // packageNames may list several, comma-separated, such as browsers: the first one installed opens.
    private void launch(String packageNames) {
        mCommands.execute(() -> {
            for (String packageName : packageNames.split(",")) {
                packageName = packageName.trim();
                String component = run("cmd", "package", "resolve-activity", "--brief", "--user", "0",
                        "-a", "android.intent.action.MAIN", "-c", "android.intent.category.LAUNCHER", packageName);
                String[] lines = component.trim().split("\n");
                String last = lines[lines.length - 1].trim();
                if (!last.contains("/")) {
                    Log.i(TAG, packageName + " isn't installed");
                    continue;
                }
                String result = run("am", "start", "--user", "0", "--display", displayId(),
                        "--activity-clear-task", "-n", last);
                mLaunched = packageName;
                Log.i(TAG, "Launched " + packageName + ": " + result.trim());
                if (!mFocusSafe)
                    returnFocus();
                return;
            }
            Log.e(TAG, "None of " + packageNames + " is installed");
        });
    }

    // Opens a web address in the app launch() opened (a browser), on the window's display.
    private void openUrl(String url) {
        mCommands.execute(() -> {
            String target = mLaunched;
            String result = target == null
                    ? run("am", "start", "--user", "0", "--display", displayId(), "-a", "android.intent.action.VIEW", "-d", url)
                    : run("am", "start", "--user", "0", "--display", displayId(), "-a", "android.intent.action.VIEW", "-d", url,
                            "-p", target);
            Log.i(TAG, "Opened " + url + ": " + result.trim());
            if (!mFocusSafe)
                returnFocus();
        });
    }

    // Gives the phone's focus back to Unity's task, which stops drawing without it. Only when it was
    // really lost: Unity asks (FOCUS, on OnApplicationFocus(false)), or the display couldn't be made
    // with STEAL_TOP_FOCUS_DISABLED. It used to run after every tap as "am start" of Unity's activity,
    // and that delivered a new intent, which pauses and resumes the activity: ARCore's session paused
    // with it, lost tracking, and recentred the room on every tap (seen on the S23 FE). setFocusedTask
    // moves focus without pausing anything; the am start is the fallback.
    private void returnFocus() {
        if (mHomeTask > 0) {
            try {
                Object service = Class.forName("android.app.ActivityTaskManager").getMethod("getService").invoke(null);
                service.getClass().getMethod("setFocusedTask", int.class).invoke(service, mHomeTask);
                Log.i(TAG, "Focus back to task " + mHomeTask);
                return;
            } catch (ReflectiveOperationException | RuntimeException e) {
                Log.w(TAG, "Couldn't set the focused task, starting the activity instead", e);
            }
        }
        run("am", "start", "--user", "0", "--display", "0", "-f", "0x20000", "-n", mHomeActivity);
    }

    // The display holding the focused task (ActivityTaskManager.getFocusedRootTaskInfo, hidden), or -1.
    private static int focusedDisplay() {
        try {
            Object service = Class.forName("android.app.ActivityTaskManager").getMethod("getService").invoke(null);
            Object info = service.getClass().getMethod("getFocusedRootTaskInfo").invoke(service);
            return info == null ? -1 : info.getClass().getField("displayId").getInt(info);
        } catch (ReflectiveOperationException | RuntimeException e) {
            Log.w(TAG, "Couldn't find the focused display", e);
            return -1;
        }
    }

    private void input(String... arguments) {
        String[] command = new String[3 + arguments.length];
        command[0] = "input";
        command[1] = "-d";
        command[2] = displayId();
        System.arraycopy(arguments, 0, command, 3, arguments.length);
        mCommands.execute(() -> {
            run(command);
            if (!mFocusSafe)
                returnFocus();
        });
    }

    // A tap, injected straight into the input system (a press and a release at the point), which is
    // immediate where the input command starts a process; the command if injecting fails.
    private void tap(int x, int y) {
        mCommands.execute(() -> {
            try {
                long down = SystemClock.uptimeMillis();
                injectTouch(MotionEvent.ACTION_DOWN, down, down, x, y);
                injectTouch(MotionEvent.ACTION_UP, down, down + 40, x, y);
                return;
            } catch (ReflectiveOperationException | RuntimeException e) {
                Log.w(TAG, "Couldn't inject a tap, using the input command", e);
            }
            run("input", "-d", displayId(), "tap", String.valueOf(x), String.valueOf(y));
            if (!mFocusSafe)
                returnFocus();
        });
    }

    private void injectTouch(int action, long downTime, long eventTime, int x, int y) throws ReflectiveOperationException {
        MotionEvent event = MotionEvent.obtain(downTime, eventTime, action, x, y, 0);
        event.setSource(InputDevice.SOURCE_TOUCHSCREEN);
        try {
            MotionEvent.class.getMethod("setDisplayId", int.class).invoke(event, mDisplay.getDisplay().getDisplayId());
            // INJECT_INPUT_EVENT_MODE_WAIT_FOR_FINISH, so the release follows the press.
            injector().invoke(inputManager(), event, 2);
        } finally {
            event.recycle();
        }
    }

    // A mouse wheel's scroll at a point, notches up (+) or down (-): scrolls lists and pages at once,
    // without touching the display, so focus stays with Unity. Injected here as shell, straight into
    // the input system: the S23 FE's input command (Android 14) has no scroll, and a process per
    // event is slow anyway. If injecting fails, a swipe of swipeDistance pixels does it instead.
    private void scroll(int x, int y, int notches, int swipeDistance) {
        mCommands.execute(() -> {
            try {
                injectWheel(x, y, notches);
                return;
            } catch (ReflectiveOperationException | RuntimeException e) {
                Log.w(TAG, "Couldn't inject a scroll, swiping instead", e);
            }
            run("input", "-d", displayId(), "swipe", String.valueOf(x), String.valueOf(y - swipeDistance),
                    String.valueOf(x), String.valueOf(y + swipeDistance), "250");
            if (!mFocusSafe)
                returnFocus();
        });
    }

    private void injectWheel(int x, int y, int notches) throws ReflectiveOperationException {
        MotionEvent.PointerProperties[] properties = { new MotionEvent.PointerProperties() };
        properties[0].id = 0;
        properties[0].toolType = MotionEvent.TOOL_TYPE_MOUSE;
        MotionEvent.PointerCoords[] coords = { new MotionEvent.PointerCoords() };
        coords[0].x = x;
        coords[0].y = y;
        coords[0].setAxisValue(MotionEvent.AXIS_VSCROLL, notches);
        long now = SystemClock.uptimeMillis();
        MotionEvent event = MotionEvent.obtain(now, now, MotionEvent.ACTION_SCROLL, 1, properties, coords, 0, 0, 1f, 1f,
                0, 0, InputDevice.SOURCE_MOUSE, 0);
        try {
            MotionEvent.class.getMethod("setDisplayId", int.class).invoke(event, mDisplay.getDisplay().getDisplayId());
            // INJECT_INPUT_EVENT_MODE_ASYNC.
            injector().invoke(inputManager(), event, 0);
        } finally {
            event.recycle();
        }
    }

    // InputManagerGlobal on Android 14 and later, InputManager before; both hidden.
    private Object mInputManager;
    private Method mInject;

    private Object inputManager() throws ReflectiveOperationException {
        if (mInputManager == null) {
            Class<?> type;
            try {
                type = Class.forName("android.hardware.input.InputManagerGlobal");
            } catch (ClassNotFoundException e) {
                type = Class.forName("android.hardware.input.InputManager");
            }
            mInputManager = type.getMethod("getInstance").invoke(null);
        }
        return mInputManager;
    }

    private Method injector() throws ReflectiveOperationException {
        if (mInject == null)
            mInject = inputManager().getClass().getMethod("injectInputEvent", InputEvent.class, int.class);
        return mInject;
    }

    // No on-screen keyboard for this display (IWindowManager.setDisplayImePolicy, DISPLAY_IME_POLICY_HIDE):
    // it would come up on the phone's own screen, over the stereo view. Hidden, and the S23 FE's wm
    // command doesn't offer it, so it's called directly.
    private static void hideIme(int displayId) {
        try {
            Object manager = Class.forName("android.view.WindowManagerGlobal").getMethod("getWindowManagerService").invoke(null);
            manager.getClass().getMethod("setDisplayImePolicy", int.class, int.class).invoke(manager, displayId, 2);
            Log.i(TAG, "No on-screen keyboard on display " + displayId);
        } catch (ReflectiveOperationException | RuntimeException e) {
            Log.w(TAG, "Couldn't turn off the on-screen keyboard for the display", e);
        }
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
