using UnityEngine;

namespace CardboardXR.Stereo
{
    // Keeps the screen bright while the app is in front: a viewer's lenses dim the picture, and the
    // phone's own brightness is set for reading at arm's length. 90% by default rather than full, to
    // spare heat; the "brightness" launch option sets another percentage (0 leaves it alone).
    //   On Android this is the app window's brightness, not the system setting, and lapses when the app
    //   leaves the front. On iOS it's the screen's, which iOS can keep after the app leaves, so the
    //   user's own brightness is noted on coming to the front and put back on going to the background or
    //   quitting. It's also saved (k_SavedKey), since an app that's killed (by installing a new build,
    //   a crash, or swiping it away) gets no chance to put it back: started again with the screen still
    //   at this app's level, it takes the saved level as the user's. iOS still dims a hot phone
    //   whatever the app asks. (The screen never sleeps while the app runs: StereoRig sets
    //   Screen.sleepTimeout.)
    // The settings panel's Controls set the level (Level), which is kept for next time (k_LevelKey);
    // the launch option, if given, wins at start. Logs [Stereo].
    public class ScreenBrightness : MonoBehaviour
    {
        const int k_DefaultPercent = 90;
        const string k_SavedKey = "UsersBrightness";
        const string k_LevelKey = "AppBrightness";
        // Never darker than this from the Controls, so the screen can't go black in the viewer.
        public const float k_MinLevel = 0.1f;

        // The one running on a phone; null in the editor or with the launch option at 0.
        public static ScreenBrightness Instance { get; private set; }

        // The app's brightness, 0 to 1: set from the settings panel, and kept for next time.
        public float Level
        {
            get => m_Level;
            set
            {
                m_Level = Mathf.Clamp(value, k_MinLevel, 1f);
                PlayerPrefs.SetFloat(k_LevelKey, m_Level);
                PlayerPrefs.Save();
                if (m_UsersLevel >= 0f)
                    Screen.brightness = m_Level;
                Debug.Log($"[Stereo] Screen brightness set to {m_Level * 100f:F0}%");
            }
        }

        float m_Level;
        float m_UsersLevel = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (!Application.isMobilePlatform)
                return;
            var percent = LaunchOptions.GetInt("brightness", -1);
            if (percent == 0)
                return;
            // The launch option, else what the Controls last set, else the default.
            var level = percent > 0 ? Mathf.Clamp01(percent / 100f) : PlayerPrefs.GetFloat(k_LevelKey, k_DefaultPercent / 100f);
            var holder = new GameObject("Screen Brightness");
            DontDestroyOnLoad(holder);
            Instance = holder.AddComponent<ScreenBrightness>();
            Instance.m_Level = Mathf.Clamp(level, k_MinLevel, 1f);
        }

        void Start()
        {
            if (m_UsersLevel < 0f)
                Brighten();
        }

        // iOS also reports an unpause at launch, so only a return from the background brightens again.
        void OnApplicationPause(bool paused)
        {
            if (paused)
                Restore();
            else if (m_UsersLevel < 0f)
                Brighten();
        }

        void OnApplicationQuit() => Restore();

        void Brighten()
        {
            if (m_UsersLevel < 0f)
            {
                var now = Screen.brightness;
                // Still at this app's level: a run that was killed left it there; the user's is the saved one.
                var leftByUs = Mathf.Abs(now - m_Level) < 0.01f && PlayerPrefs.HasKey(k_SavedKey);
                m_UsersLevel = leftByUs ? PlayerPrefs.GetFloat(k_SavedKey) : now;
                PlayerPrefs.SetFloat(k_SavedKey, m_UsersLevel);
                PlayerPrefs.Save();
            }
            Screen.brightness = m_Level;
            Debug.Log($"[Stereo] Screen brightness {m_Level * 100f:F0}% (the user's is {m_UsersLevel * 100f:F0}%)");
        }

        void Restore()
        {
            if (m_UsersLevel < 0f)
                return;
            Screen.brightness = m_UsersLevel;
            m_UsersLevel = -1f;
        }
    }
}
