using UnityEngine;

namespace CardboardXR.Demos.Apps
{
    // Public-API bridge. Android opens apps and URLs normally; it does not mirror them into Unity.
    public sealed class AppWindowBridge
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass m_Plugin;
        AndroidJavaClass Plugin => m_Plugin ??= new AndroidJavaClass("com.brenpoly.cardboardxr.windows.AppWindows");
#endif

        public bool LaunchPackage(string packageName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Plugin.CallStatic<bool>("launchPackage", packageName);
#else
            Debug.Log($"[Apps] Android app launch requested: {packageName}");
            return false;
#endif
        }

        public bool OpenUrl(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Plugin.CallStatic<bool>("openUrl", url);
#else
            Debug.Log($"[Apps] URL requested: {url}");
            return false;
#endif
        }

        public bool OpenMaps()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Plugin.CallStatic<bool>("openMaps");
#else
            return OpenUrl("https://www.google.com/maps");
#endif
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin?.Dispose();
            m_Plugin = null;
#endif
        }
    }
}
