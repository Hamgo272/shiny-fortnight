using UnityEngine;

namespace CardboardXR.Demos.Apps
{
    public enum AppWindowState
    {
        Failed = -3, PermissionDenied = -2, NoShizuku = -1,
        Idle = 0, WaitingForPermission = 1, Starting = 2, Running = 3
    }

    // Unity adapter for the embedded Android WebView backend.
    public sealed class AppWindowBridge
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass m_Plugin;
        byte[] m_LastFrame;
        long m_Sequence;
        AppWindowState m_State = AppWindowState.Idle;
        AndroidJavaClass Plugin => m_Plugin ??= new AndroidJavaClass("com.glassesxr.cardboardxr.GlassesWebViewBridge");
#endif

        public AppWindowState State
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return m_State;
#else
                return AppWindowState.Failed;
#endif
            }
        }

        public long Sequence
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                if (m_State == AppWindowState.Running)
                {
                    try
                    {
                        var frame = Plugin.CallStatic<byte[]>("copyFrame");
                        if (frame != null && frame.Length > 0)
                        {
                            m_LastFrame = frame;
                            m_Sequence++;
                        }
                    }
                    catch (AndroidJavaException e)
                    {
                        Debug.LogWarning("[Apps] WebView frame capture failed: " + e.Message);
                        m_State = AppWindowState.Failed;
                    }
                }
                return m_Sequence;
#else
                return 0;
#endif
            }
        }

        public void Start(int width, int height, int dpi, string launchPackage)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_State = AppWindowState.Starting;
            m_LastFrame = null;
            m_Sequence = 0;
            try
            {
                if (Plugin.CallStatic<int>("create", width, height) == 0)
                {
                    m_State = AppWindowState.Failed;
                    Debug.LogError("[Apps] Android WebView could not create its virtual display.");
                    return;
                }
                m_State = AppWindowState.Running;
                var initialUrl = launchPackage != null && launchPackage.Contains("com.google.android.youtube")
                    ? "https://m.youtube.com/" : "https://www.google.com/";
                Plugin.CallStatic("load", initialUrl);
            }
            catch (AndroidJavaException e)
            {
                m_State = AppWindowState.Failed;
                Debug.LogError("[Apps] Could not start embedded WebView: " + e.Message);
            }
#endif
        }

        public byte[] Frame(long sequence)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return m_LastFrame ?? new byte[0];
#else
            return new byte[0];
#endif
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try { if (m_Plugin != null) m_Plugin.CallStatic("destroy"); }
            catch (AndroidJavaException e) { Debug.LogWarning("[Apps] WebView shutdown failed: " + e.Message); }
            m_Plugin?.Dispose();
            m_Plugin = null;
            m_LastFrame = null;
            m_Sequence = 0;
            m_State = AppWindowState.Idle;
#endif
        }

        public void Launch(string packageName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (packageName != null && packageName.Contains("com.google.android.youtube"))
                OpenUrl("https://m.youtube.com/");
#endif
        }

        public void Tap(int x, int y)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_State == AppWindowState.Running) Plugin.CallStatic("tap", (float)x, (float)y);
#endif
        }

        public void Swipe(int fromX, int fromY, int toX, int toY, int milliseconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_State != AppWindowState.Running) return;
            Plugin.CallStatic("touchDown", (float)fromX, (float)fromY);
            Plugin.CallStatic("touchMove", (float)toX, (float)toY);
            Plugin.CallStatic("touchUp", (float)toX, (float)toY);
#endif
        }

        public void Scroll(int x, int y, int notches, int swipeDistance)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_State == AppWindowState.Running)
                Plugin.CallStatic("scroll", (float)x, (float)y, (float)-swipeDistance);
#endif
        }

        public void Focus() { }
        public void Paused(bool paused) { }

        public void Text(string text)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_State != AppWindowState.Running || text == null) return;
            var escaped = text.Replace("\\", "\\\\").Replace("'", "\\'");
            Plugin.CallStatic("evaluate",
                "(function(t){var e=document.activeElement;if(!e)return;" +
                "if('value' in e){e.value=(e.value||'')+t;e.dispatchEvent(new Event('input',{bubbles:true}));}" +
                "else if(e.isContentEditable)document.execCommand('insertText',false,t);})('" + escaped + "')");
#endif
        }

        public void OpenUrl(string url)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_State == AppWindowState.Running && !string.IsNullOrEmpty(url))
                Plugin.CallStatic("load", url);
#endif
        }

        public void Key(int keyCode)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (m_State != AppWindowState.Running) return;
            if (keyCode == 4) Plugin.CallStatic("back");
            else if (keyCode == 66)
                Plugin.CallStatic("evaluate",
                    "(function(){var e=document.activeElement;if(!e)return;" +
                    "e.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',code:'Enter',keyCode:13,bubbles:true}));" +
                    "if(e.form){if(e.form.requestSubmit)e.form.requestSubmit();else e.form.submit();}" +
                    "e.dispatchEvent(new KeyboardEvent('keyup',{key:'Enter',code:'Enter',keyCode:13,bubbles:true}));})()");
#endif
        }
    }
}
