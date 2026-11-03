using System;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardboardXR.Demos.Apps
{
    // A floating window onto a real Android app: the app runs on a private virtual display, its picture
    // comes here through AppWindowBridge, and a pinch or tap with the gaze on the window becomes a tap
    // at that point in the app. The buttons below it go back, go to the app's start, scroll, bring the
    // window back in front, and leave. It centers itself on the first frame, since the head's first
    // pose can be off until the tracker settles. Needs Shizuku running on the phone.
    // The demo runs in 6DoF passthrough with hand tracking, so the window floats in the room and a
    // pinch clicks. It was 3DoF with hands off, to save heat for the app's own video decoding, but
    // turning on passthrough from the settings panel then left no pinch source (see PinchInput).
    // Logs [Apps].
    public class AppWindowDemo : Demo
    {
        const string k_App = "com.google.android.youtube";
        const int k_Width = 1280;
        const int k_Height = 720;
        const int k_Dpi = 200;
        const int k_KeyBack = 4;
        const int k_SwipeMilliseconds = 250;

        const float k_WindowWidth = 2.4f;
        const float k_WindowDistance = 2f;
        const float k_PanelDistance = 1.5f;
        const float k_PanelPitch = -30f;
        const float k_LogSeconds = 5f;
        static readonly Vector2 k_PanelSize = new(700f, 112f);

        [Tooltip("An unlit material for the app's picture. Copied at runtime.")]
        [SerializeField] Material m_Material;

        CoreServices m_Core;
        readonly AppWindowBridge m_Bridge = new();
        Transform m_Window;
        Material m_WindowMaterial;
        Texture2D m_Texture;
        RectTransform m_Panel;
        TMP_Text m_Status;
        AppWindowState m_State = AppWindowState.Idle;
        long m_Sequence;
        long m_LoggedSequence;
        float m_NextLog;

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
            BuildWindow();
            BuildPanel();
            PlaceWindow();
            m_Bridge.Start(k_Width, k_Height, k_Dpi, k_App);
        }

        public override void OnDemoExit(CoreServices core)
        {
            m_Bridge.Stop();
            Destroy(m_Texture);
            Destroy(m_WindowMaterial);
        }

        void Update()
        {
            var state = m_Bridge.State;
            if (state != m_State)
            {
                m_State = state;
                m_Status.text = Describe(state);
                Debug.Log($"[Apps] {state}");
            }

            var sequence = m_Bridge.Sequence;
            if (sequence != m_Sequence)
            {
                if (m_Sequence == 0)
                    PlaceWindow();
                m_Sequence = sequence;
                m_Texture.LoadRawTextureData(m_Bridge.Frame(sequence));
                m_Texture.Apply(false);
            }
            if (Time.unscaledTime >= m_NextLog)
                LogFrames();
        }

        // How many frames came in, and how bright the picture is: 0 means they're arriving but black.
        void LogFrames()
        {
            m_NextLog = Time.unscaledTime + k_LogSeconds;
            if (m_Sequence == 0)
            {
                Debug.Log("[Apps] No frames yet");
                return;
            }
            var frame = m_Bridge.Frame(m_Sequence);
            var total = 0L;
            var samples = 0;
            for (var i = 0; i < frame.Length; i += 4004)
            {
                total += frame[i];
                samples++;
            }
            Debug.Log($"[Apps] {(m_Sequence - m_LoggedSequence) / k_LogSeconds:F0} frames/s, {m_Sequence} in all, " +
                      $"mean red {total / samples} of 255");
            m_LoggedSequence = m_Sequence;
        }

        static string Describe(AppWindowState state) => state switch
        {
            AppWindowState.NoShizuku => "Open Shizuku and start it, then come back here.",
            AppWindowState.WaitingForPermission => "Allow CardboardXR in the Shizuku prompt on the phone.",
            AppWindowState.PermissionDenied => "Shizuku permission was denied. Allow CardboardXR in the Shizuku app.",
            AppWindowState.Failed => "Couldn't open the window. Restart Shizuku and try again.",
            AppWindowState.Starting => "Opening the window...",
            _ => "",
        };

        void BuildWindow()
        {
            m_Texture = new Texture2D(k_Width, k_Height, TextureFormat.RGBA32, false);
            // The picture's first row is its top; a texture's is its bottom.
            m_WindowMaterial = new Material(m_Material)
            {
                mainTexture = m_Texture,
                mainTextureScale = new Vector2(1f, -1f),
                mainTextureOffset = new Vector2(0f, 1f),
            };
            var window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "App Window";
            window.transform.SetParent(transform, false);
            window.transform.localScale = new Vector3(k_WindowWidth, k_WindowWidth * k_Height / k_Width, 1f);
            window.GetComponent<MeshRenderer>().sharedMaterial = m_WindowMaterial;
            window.AddComponent<WindowClick>().Clicked += OnWindowClicked;
            m_Window = window.transform;
        }

        void BuildPanel()
        {
            m_Panel = WorldUi.Panel("Window Panel", transform, k_PanelSize, k_PanelDistance);
            m_Status = M3.Text("Status", m_Panel, "", M3Theme.Type.BodyMedium, new Vector2(0f, 34f), new Vector2(580f, 24f),
                M3Theme.OnSurfaceVariant);
            M3.Button("Back", m_Panel, "Back", M3Button.Variant.Tonal, new Vector2(-290f, -20f), () => m_Bridge.Key(k_KeyBack), width: 104f);
            M3.Button("Home", m_Panel, "Home", M3Button.Variant.Tonal, new Vector2(-174f, -20f), () => m_Bridge.Launch(k_App), width: 104f);
            M3.Button("Scroll Up", m_Panel, "Up", M3Button.Variant.Tonal, new Vector2(-58f, -20f), () => Scroll(1), width: 104f);
            M3.Button("Scroll Down", m_Panel, "Down", M3Button.Variant.Tonal, new Vector2(58f, -20f), () => Scroll(-1), width: 104f);
            M3.Button("Center", m_Panel, "Center", M3Button.Variant.Tonal, new Vector2(174f, -20f), PlaceWindow, width: 104f);
            M3.Button("Leave", m_Panel, "Leave", M3Button.Variant.Text, new Vector2(290f, -20f), () => m_Core.Demos.ExitToLobby(), width: 104f);
        }

        // Ahead of the head at eye level, with the panel below it.
        void PlaceWindow()
        {
            var head = m_Core.Head;
            var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized) : Quaternion.identity;
            m_Window.SetPositionAndRotation(head.position + yaw * Vector3.forward * k_WindowDistance, yaw);
            WorldUi.PlaceInFront(m_Panel, head, k_PanelDistance, k_PanelPitch);
        }

        // Moves by half the window's height: direction +1 shows what's above, -1 what's below.
        void Scroll(int direction)
        {
            var distance = k_Height / 4 * direction;
            m_Bridge.Swipe(k_Width / 2, k_Height / 2 - distance, k_Width / 2, k_Height / 2 + distance, k_SwipeMilliseconds);
        }

        void OnWindowClicked()
        {
            var ray = new Ray(m_Core.Head.position, m_Core.Head.forward);
            var plane = new Plane(m_Window.forward, m_Window.position);
            if (!plane.Raycast(ray, out var distance))
                return;
            var pixel = PixelAt(m_Window.InverseTransformPoint(ray.GetPoint(distance)), k_Width, k_Height);
            Debug.Log($"[Apps] Tap at {pixel.x}, {pixel.y}");
            m_Bridge.Tap(pixel.x, pixel.y);
        }

        // The pixel, from the top left, at a point in a unit quad's local space, which runs -0.5 to 0.5.
        public static Vector2Int PixelAt(Vector3 local, int width, int height) => new(
            Mathf.Clamp(Mathf.RoundToInt((local.x + 0.5f) * width), 0, width - 1),
            Mathf.Clamp(Mathf.RoundToInt((0.5f - local.y) * height), 0, height - 1));
    }

    // Passes a gaze click on the window's collider to the demo.
    public class WindowClick : MonoBehaviour, IPointerClickHandler
    {
        public event Action Clicked;

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
