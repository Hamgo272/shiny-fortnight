using CardboardXR.Demos;
using CardboardXR.UI;
using UnityEngine;

namespace CardboardXR.Demos.Apps
{
    // Android's public APIs cannot capture arbitrary apps into Unity-managed virtual displays.
    // This demo therefore provides launch shortcuts; selected apps open as normal Android activities.
    public class AppWindowDemo : Demo
    {
        public const string BrowserId = "browser";
        const string k_Chrome = "com.android.chrome";
        const string k_SamsungInternet = "com.sec.android.app.sbrowser";
        const string k_YouTube = "com.google.android.youtube";

        AppWindowBridge m_Bridge;
        RectTransform m_Panel;

        public override void OnDemoEnter(CoreServices core)
        {
            core.Placer.ObjectPrefab = null;
            m_Bridge = new AppWindowBridge();
            m_Panel = WorldUi.Panel("Android Apps", transform, new Vector2(900f, 440f), 1.6f);
            M3.Text("Title", m_Panel, "Open Android apps", M3Theme.Type.HeadlineSmall,
                new Vector2(0f, 150f), new Vector2(800f, 56f), M3Theme.OnSurface);
            M3.Text("Description", m_Panel,
                "Apps open normally in Android. They are not mirrored as interactive VR windows.",
                M3Theme.Type.BodyMedium, new Vector2(0f, 95f), new Vector2(800f, 48f), M3Theme.OnSurfaceVariant);

            const float width = 180f;
            const float gap = 18f;
            var x = -(width + gap);
            M3.Button("Chrome", m_Panel, "Chrome", M3Button.Variant.Filled,
                new Vector2(x, 20f), () => LaunchBrowser(k_Chrome), width: width);
            M3.Button("Samsung Internet", m_Panel, "Internet", M3Button.Variant.Filled,
                new Vector2(0f, 20f), () => LaunchBrowser(k_SamsungInternet), width: width);
            M3.Button("YouTube", m_Panel, "YouTube", M3Button.Variant.Filled,
                new Vector2(width + gap, 20f), () => Launch(k_YouTube), width: width);
            M3.Button("Google Maps", m_Panel, "Maps", M3Button.Variant.Filled,
                new Vector2(x, -75f), () => m_Bridge.OpenMaps(), width: width);
            M3.Button("Open Web", m_Panel, "Open Google", M3Button.Variant.Tonal,
                new Vector2(0f, -75f), () => m_Bridge.OpenUrl("https://www.google.com"), width: width);
            M3.Button("Back to Lobby", m_Panel, "Leave", M3Button.Variant.Text,
                new Vector2(width + gap, -75f), () => core.Demos.ExitToLobby(), width: width);
        }

        void LaunchBrowser(string packageName)
        {
            if (!m_Bridge.LaunchPackage(packageName))
                m_Bridge.OpenUrl("https://www.google.com");
        }

        void Launch(string packageName)
        {
            if (!m_Bridge.LaunchPackage(packageName))
                Debug.LogWarning($"[Apps] Could not launch {packageName}. Is it installed?");
        }

        public override void OnDemoExit(CoreServices core)
        {
            m_Bridge?.Dispose();
            if (m_Panel != null)
                Destroy(m_Panel.gameObject);
        }
    }
}
