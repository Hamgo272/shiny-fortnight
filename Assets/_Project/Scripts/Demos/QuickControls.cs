using CardboardXR.Stereo;
using CardboardXR.UI;
using TMPro;
using UnityEngine;

namespace CardboardXR.Demos
{
    // The settings panel's Controls: a card under the panel with the phone's media volume and the
    // screen's brightness, each a slider (pinch anywhere along it to set it there) between - and +
    // steps, and its percentage. The toolbar's Controls button shows and hides it. Volume is Android's
    // music stream, which YouTube and the browser on a window play on too, so it sets theirs as well;
    // brightness is the app's own (ScreenBrightness), kept for next time. The card reads both again
    // each time it's shown, so the phone's own volume buttons are reflected. Part of the settings
    // panel, so it follows the panel and closes with it. Lives on the always-active Demos object.
    // Logs [Demos].
    public class QuickControls : MonoBehaviour
    {
        // In dp.
        const float k_Width = 520f;
        const float k_RowHeight = 64f;
        const float k_Padding = 16f;
        const float k_SliderWidth = 260f;
        // Below the panel's own X, which sits under the panel.
        const float k_Below = 20f + M3Theme.k_Target + 16f;
        // One press of - or +.
        const float k_Step = 0.1f;

        [Tooltip("The settings panel, which the card hangs under.")]
        [SerializeField] RectTransform m_Panel;
        [Tooltip("The toolbar's Controls button, shown selected while the card shows.")]
        [SerializeField] M3Button m_Button;

        RectTransform m_Root;
        M3Slider m_Volume;
        M3Slider m_Brightness;
        TMP_Text m_VolumeValue;
        TMP_Text m_BrightnessValue;

        public bool IsShown => m_Root != null && M3Fade.IsShown(m_Root.gameObject);

        // The toolbar's Controls button.
        public void Toggle()
        {
            if (m_Root == null)
                Build();
            var show = !IsShown;
            if (show)
                Refresh();
            M3Fade.SetShown(m_Root.gameObject, show);
            if (m_Button != null)
                m_Button.Selected = show;
            Debug.Log($"[Demos] Controls {(show ? "shown" : "hidden")}");
        }

        void Build()
        {
            var height = 2f * k_RowHeight + 2f * k_Padding;
            m_Root = M3.Surface("Controls", m_Panel, new Vector2(0f, -(m_Panel.rect.height / 2f + k_Below + height / 2f)),
                new Vector2(k_Width, height), M3Theme.SurfaceContainerHigh, M3Theme.k_ShapePanel, raycastTarget: true).rectTransform;
            m_Root.gameObject.AddComponent<M3Fade>();
            var top = height / 2f - k_Padding - k_RowHeight / 2f;
            (m_Volume, m_VolumeValue) = Row("Volume", "volume_up", top, SetVolume);
            (m_Brightness, m_BrightnessValue) = Row("Brightness", "brightness_6", top - k_RowHeight, SetBrightness);
            m_Root.gameObject.SetActive(false);
        }

        // A row: icon, -, slider, +, percentage, left to right.
        (M3Slider, TMP_Text) Row(string name, string icon, float y, System.Action<float> set)
        {
            var left = -k_Width / 2f + k_Padding;
            M3.Icon($"{name} Icon", m_Root, icon, 28f, new Vector2(left + 20f, y), M3Theme.OnSurfaceVariant);
            var slider = M3.Slider($"{name} Slider", m_Root, new Vector2(left + 20f + 40f + 48f + k_SliderWidth / 2f, y), k_SliderWidth);
            slider.Clicked += set;
            M3.IconButton($"{name} Down", m_Root, "remove", M3Button.Variant.Text, new Vector2(left + 20f + 40f + 4f, y),
                () => set(slider.Value - k_Step), 48f);
            M3.IconButton($"{name} Up", m_Root, "add", M3Button.Variant.Text, new Vector2(left + 20f + 40f + 48f + k_SliderWidth + 44f, y),
                () => set(slider.Value + k_Step), 48f);
            var value = M3.Text($"{name} Value", m_Root, "", M3Theme.Type.LabelLarge, new Vector2(k_Width / 2f - k_Padding - 28f, y),
                new Vector2(56f, k_RowHeight), M3Theme.OnSurfaceVariant);
            return (slider, value);
        }

        void Refresh()
        {
            Show(m_Volume, m_VolumeValue, DeviceVolume.Get());
            var brightness = ScreenBrightness.Instance;
            Show(m_Brightness, m_BrightnessValue, brightness != null ? brightness.Level : Screen.brightness);
        }

        static void Show(M3Slider slider, TMP_Text label, float value)
        {
            slider.Value = value;
            label.text = $"{Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%";
        }

        void SetVolume(float value)
        {
            value = Mathf.Clamp01(value);
            DeviceVolume.Set(value);
            Show(m_Volume, m_VolumeValue, DeviceVolume.Get());
            Debug.Log($"[Demos] Volume {m_VolumeValue.text}");
        }

        void SetBrightness(float value)
        {
            var brightness = ScreenBrightness.Instance;
            if (brightness != null)
            {
                brightness.Level = value;
                value = brightness.Level;
            }
            else
            {
                value = Mathf.Clamp(value, ScreenBrightness.k_MinLevel, 1f);
                Screen.brightness = value;
            }
            Show(m_Brightness, m_BrightnessValue, value);
        }
    }

    // The phone's media volume (Android's music stream), 0 to 1, in the stream's own steps. Off
    // Android it's kept here only, so the card still works in the editor.
    public static class DeviceVolume
    {
        const int k_MusicStream = 3;
        static float s_EditorValue = 0.5f;

        public static float Get()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var audio = AudioManager();
                var max = audio.Call<int>("getStreamMaxVolume", k_MusicStream);
                return max > 0 ? (float)audio.Call<int>("getStreamVolume", k_MusicStream) / max : 0f;
            }
            catch (AndroidJavaException e)
            {
                Debug.LogWarning($"[Demos] Couldn't read the volume: {e.Message}");
                return 0f;
            }
#else
            return s_EditorValue;
#endif
        }

        public static void Set(float value)
        {
            value = Mathf.Clamp01(value);
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var audio = AudioManager();
                var max = audio.Call<int>("getStreamMaxVolume", k_MusicStream);
                // No flags: Android's own volume bar would come up on the phone's screen, over the view.
                audio.Call("setStreamVolume", k_MusicStream, Mathf.RoundToInt(value * max), 0);
            }
            catch (AndroidJavaException e)
            {
                Debug.LogWarning($"[Demos] Couldn't set the volume: {e.Message}");
            }
#else
            s_EditorValue = value;
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject AudioManager()
        {
            using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
            using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
            return activity.Call<AndroidJavaObject>("getSystemService", "audio");
        }
#endif
    }
}
