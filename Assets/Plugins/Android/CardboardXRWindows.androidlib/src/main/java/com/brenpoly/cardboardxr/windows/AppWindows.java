package com.brenpoly.cardboardxr.windows;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

public final class AppWindows {
    private static final String TAG = "CardboardXRApps";

    private AppWindows() {}

    // Android's public app-launch API: the target app opens normally, outside Unity.
    public static boolean launchPackage(String packageName) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null || packageName == null || packageName.isEmpty()) return false;
        try {
            Intent intent = activity.getPackageManager().getLaunchIntentForPackage(packageName);
            if (intent == null) {
                Log.w(TAG, "App is not installed: " + packageName);
                return false;
            }
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (RuntimeException e) {
            Log.e(TAG, "Couldn't launch " + packageName, e);
            return false;
        }
    }

    // Android resolves the URL to the user's installed browser using the normal chooser/default.
    public static boolean openUrl(String url) {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null || url == null || url.isEmpty()) return false;
        try {
            Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity.startActivity(intent);
            return true;
        } catch (RuntimeException e) {
            Log.e(TAG, "Couldn't open URL " + url, e);
            return false;
        }
    }

    public static boolean openMaps() {
        return openUrl("https://www.google.com/maps");
    }
}
