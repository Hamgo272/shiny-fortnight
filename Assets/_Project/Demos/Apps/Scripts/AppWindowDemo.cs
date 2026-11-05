using System;
using CardboardXR.Hands;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using CardboardXR.Demos.Portal;

namespace CardboardXR.Demos.Apps
{
    // A floating window onto a real Android app: the app runs on a private virtual display, its picture
    // comes here through AppWindowBridge, and a pinch (or screen tap) with the gaze on the window taps
    // the app there. As in the first version, that's the app's ordinary gaze click (WindowClick): the
    // demo doesn't take pinches on the window. An earlier version did, to drag and to resize from
    // every edge, and taps kept turning into drags, resizes (the window lit blue and moved) and, with
    // the palm half toward the face, the settings panel.
    // Only two pinches are the demo's (PinchRouter.Handler): on one of the L handles just outside the
    // bottom corners, which resizes the window from that corner as in Android XR, the app's picture
    // staying 16:9; and, after Move, the one that puts the window down. The window otherwise stays
    // where it is: only Move, Arrange or a handle shift it.
    // Two round buttons sit in a small bar above the window: Move (left) makes the window follow the
    // gaze until a pinch puts it down; Close (right) closes the app and leaves. The panel below
    // goes back, goes to the app's start, scrolls, brings the window back in front, and leaves. It
    // centers itself on the first frame, since the head's first pose can be off until the tracker
    // settles. Needs Shizuku running on the phone.
    // The demo runs in 6DoF passthrough with hand tracking, so the window floats in the room and the
    // hand's position drives drags and resizes. It was 3DoF with hands off, to save heat for the app's
    // own video decoding, but turning on passthrough from the settings panel then left no pinch
    // source (see PinchInput). In 3DoF a pinch has no position, so it can only tap. Logs [Apps].
    // The same demo is the Browser: its catalog entry ("browser") points at this scene, and the window
    // then opens a browser (Chrome, or Samsung Internet if there's no Chrome) instead of YouTube, with
    // a Type button on the panel for the keyboard (WindowKeyboard) and Home going to k_BrowserHome.
    public class AppWindowDemo : Demo, IPinchHandler
    {
        const string k_YouTube = "com.google.android.youtube";
        // Tried in order: the first one installed opens (WindowService.launch).
        const string k_Browsers = "com.android.chrome,com.sec.android.app.sbrowser";
        const string k_BrowserHome = "https://www.google.com";
        public const string BrowserId = "browser";
        const int k_Width = 1280;
        const int k_Height = 720;
        const int k_Dpi = 200;
        const int k_KeyBack = 4;
        const int k_KeyEnter = 66;
        // The keyboard sits below the eyes, above where the panel was, clear of most of the window.
        const float k_KeyboardPitch = -24f;
        // Up and Down: this many mouse-wheel notches, about a third of a page in Chrome or YouTube.
        const int k_ScrollNotches = 5;

        const float k_WindowWidth = 2.4f;
        const float k_MinWidth = 0.8f;
        const float k_MaxWidth = 6f;
        const float k_WindowDistance = 2f;
        const float k_PanelDistance = 1.5f;
        const float k_PanelPitch = -30f;
        const float k_LogSeconds = 5f;
        // How near a bottom corner's handle the gaze must be to take it, in meters on the window.
        const float k_HandleReach = 0.15f;
        // The hand's movement is scaled up to the window's, as the window is farther than the hand,
        // but at most this much: at the full ratio (about 7 at arm's length from a window 2 m away)
        // a few centimeters of hand flung a drag to the window's edge and a resize to its limit.
        const float k_MaxReach = 3f;
        // Heat: the app's video decoding and the frame copy come on top of AR and hand tracking, which
        // took the S23 FE to thermal status 4 (severe) with its display down to 60 Hz. So while the
        // window is up the app aims for 60 fps, put back on leaving. (Hand tracking held to 15 Hz made
        // the pinch point jumpy, and taps became drags; it runs at its usual rate.)
        const int k_FrameRate = 60;
        const float k_PanelHeight = 112f;
        // The panel's width in dp: as wide as its buttons need (BuildPanel).
        float m_PanelWidth = 700f;
        // The Moon cinema: the Moon instead of the room, passthrough off, in 6DoF with hands (the user
        // asked for 6DoF: you can lean and walk, and two-finger scroll and the fist grab work; 3DoF ran
        // cooler). The Moon's ground goes on the real floor if the room scan found it, else this far
        // below the eyes.
        const float k_MoonEyeHeight = 1.5f;
        bool m_OnMoon;
        GameObject m_MoonRoot;
        M3Button m_MoonButton;
        static readonly Vector2 k_CornerSize = new(64f, 64f);

        [Tooltip("An unlit material for the app's picture. Copied at runtime.")]
        [SerializeField] Material m_Material;
        [Tooltip("The Moon's assets, for the Moon cinema (MoonWorld, Lite). None hides the Moon button.")]
        [SerializeField] MoonAssets m_MoonAssets;

        enum Gesture
        {
            None,
            Resize,
            Grab,
        }

        // Off (see Update): taps read as fists. The fist grab, as Android XR's: look at the window and close your hand into a fist, and the
        // window follows the hand round you at its distance (the hand's movement scaled up by
        // k_GrabReach); open the hand to let it go. Needs hand tracking (6DoF), so not in the Moon
        // cinema. A finger is curled when its tip is nearer the palm's middle than k_CurledRatio palm
        // lengths, straight beyond k_StraightRatio; each shape must hold k_HandShapeSeconds. A fist
        // closing passes through a pinch, and the pinch's release comes when the hand opens: taps on
        // the window are ignored while grabbed and k_ScrollTapGuard after (the parked version held
        // every tap back 150 ms instead, which changed how taps felt).
        const float k_CurledRatio = 0.8f;
        const float k_StraightRatio = 1.05f;
        const float k_HandShapeSeconds = 0.08f;
        const float k_GrabReach = 3f;
        readonly float[] m_Ratios = new float[4];
        float m_FistFor;
        float m_OpenFor;
        float m_LostSince = -1f;
        Vector3 m_GrabHand;
        Vector3 m_GrabWindow;
        Vector3 m_GrabPanel;

        CoreServices m_Core;
        readonly AppWindowBridge m_Bridge = new();
        string m_App = k_YouTube;
        bool m_IsBrowser;
        WindowKeyboard m_Keyboard;
        readonly TwoFingerScroll m_TwoFingers = new();
        // Two-finger notches are sent at most this often, in seconds, summed meanwhile: a browser
        // animates each wheel notch, and one a frame made it stutter and fight itself.
        const float k_ScrollSendSeconds = 0.12f;
        int m_PendingNotches;
        // Taps on the window are ignored while the two-finger pose is held and this long after, in
        // seconds: going into or out of it, the thumb can brush the index finger, which is a pinch.
        const float k_ScrollTapGuard = 0.5f;
        float m_TwoFingersAt = float.NegativeInfinity;
        Vector2Int m_PendingScrollAt;
        float m_NextScrollSend;
        // Feedback the moment a tap registers, before the app answers: a ring spreading from the
        // tapped point (k_RippleSeconds), drawn with the reticle's shader.
        const float k_RippleSeconds = 0.35f;
        const float k_RippleMeters = 0.12f;
        Transform m_Ripple;
        Material m_RippleMaterial;
        float m_RippleAt = float.NegativeInfinity;
        static readonly int k_RippleInner = Shader.PropertyToID("_Inner");
        static readonly int k_RippleOuter = Shader.PropertyToID("_Outer");
        static readonly int k_RippleColor = Shader.PropertyToID("_Color");
        // The window's frame: lit while it's being scrolled, moved or resized, faint while the
        // two-finger pose is held and about to scroll.
        static readonly Color k_EdgeActive = new(0.55f, 0.8f, 1f);
        static readonly Color k_EdgeReady = new(0.3f, 0.42f, 0.55f);
        Transform m_Window;
        Transform m_Edge;
        readonly Transform[] m_Handles = new Transform[4];
        Material m_HandleMaterial;
        Material m_WindowMaterial;
        Material m_EdgeMaterial;
        Texture2D m_Texture;
        RectTransform m_Panel;
        RectTransform m_MoveCorner;
        RectTransform m_CloseCorner;
        TMP_Text m_Status;
        AppWindowState m_State = AppWindowState.Idle;
        long m_Sequence;
        long m_LoggedSequence;
        float m_NextLog;
        float m_Width = k_WindowWidth;
        bool m_Moving;
        int m_SavedFrameRate;
        FloatingWindows.Window m_WindowEntry;
        FloatingWindows.Window m_PanelEntry;

        // The pinch held on the window, if any.
        Gesture m_Gesture;
        Vector3 m_PinchStart;
        Vector2 m_ResizeSide;
        float m_ResizeFrom;

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
            m_IsBrowser = core.Demos.Current != null && core.Demos.Current.Id == BrowserId;
            m_App = m_IsBrowser ? k_Browsers : k_YouTube;
            Debug.Log($"[Apps] Opening {(m_IsBrowser ? "the browser" : "YouTube")}");
            // No cubes: a pinch that misses the window and lands on the floor would place one, as in
            // the lobby (demos get cubes unless they choose otherwise, see DemoManager).
            core.Placer.ObjectPrefab = null;
            BuildWindow();
            BuildPanel();
            BuildCorners();
            PlaceWindow();
            // For the settings panel's Arrange: the window, with its corner buttons, and its panel.
            m_WindowEntry = FloatingWindows.Register("App Window", () => m_Width, pose =>
            {
                if (m_Moving)
                    FinishMove();
                m_Window.SetPositionAndRotation(pose.position, pose.rotation);
                PlaceCorners();
            }, false, k_WindowDistance);
            m_PanelEntry = FloatingWindows.Register("Window Panel", m_Panel, M3Theme.DpToMeters(m_PanelWidth, k_PanelDistance), true,
                k_PanelDistance);
            PinchRouter.Handler = this;
            m_SavedFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = k_FrameRate;
            Debug.Log($"[Apps] To keep cool: {k_FrameRate} fps");
            m_Bridge.Start(k_Width, k_Height, k_Dpi, m_App);
        }

        public override void OnDemoExit(CoreServices core)
        {
            m_Bridge.Stop();
            Application.targetFrameRate = m_SavedFrameRate;
            FloatingWindows.Unregister(m_WindowEntry);
            FloatingWindows.Unregister(m_PanelEntry);
            Destroy(m_Texture);
            Destroy(m_WindowMaterial);
            Destroy(m_EdgeMaterial);
            Destroy(m_HandleMaterial);
            Destroy(m_RippleMaterial);
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
            if (m_Moving)
                PlaceWhereLooking();
            FollowPinch();
            FollowTwoFingers();
            // No fist grab: a pinch curls the fingers enough to read as a fist, so every tap on the
            // window grabbed it (lit blue and followed the hand). The user wants the window moved
            // only with the Move button above it. FollowFist is kept, unused, if it comes back with
            // a stricter fist test.
            ShowEdge();
            ShowRipple();
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
            AppWindowState.NoShizuku => "Embedded browser is unavailable on this device.",
            AppWindowState.WaitingForPermission => "Starting the embedded browser...",
            AppWindowState.PermissionDenied => "Android denied access to the embedded browser.",
            AppWindowState.Failed => "Couldn't start the embedded browser. Check the Android WebView installation.",
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
            window.GetComponent<MeshRenderer>().sharedMaterial = m_WindowMaterial;
            window.AddComponent<WindowClick>().Clicked += OnWindowClicked;
            m_Window = window.transform;

            // A light frame just behind the window, a little larger, shown while the gaze is on the edge.
            var edge = GameObject.CreatePrimitive(PrimitiveType.Quad);
            edge.name = "Window Edge";
            Destroy(edge.GetComponent<Collider>());
            edge.transform.SetParent(m_Window, false);
            edge.transform.localPosition = new Vector3(0f, 0f, 0.004f);
            m_EdgeMaterial = new Material(m_Material) { color = new Color(0.55f, 0.8f, 1f) };
            edge.GetComponent<MeshRenderer>().sharedMaterial = m_EdgeMaterial;
            edge.SetActive(false);
            m_Edge = edge.transform;

            // Resize handles on the bottom corners, as Android XR has: an L of two bars at each,
            // just outside the window, light blue while the gaze is on that corner.
            m_HandleMaterial = new Material(m_Material) { color = new Color(0.75f, 0.78f, 0.82f) };
            for (var i = 0; i < m_Handles.Length; i++)
            {
                var bar = GameObject.CreatePrimitive(PrimitiveType.Quad);
                bar.name = $"Resize Handle {i}";
                Destroy(bar.GetComponent<Collider>());
                bar.transform.SetParent(transform, false);
                bar.GetComponent<MeshRenderer>().sharedMaterial = m_HandleMaterial;
                m_Handles[i] = bar.transform;
            }
            ApplySize();
        }

        void ApplySize()
        {
            var height = m_Width * k_Height / k_Width;
            m_Window.localScale = new Vector3(m_Width, height, 1f);
            // The frame sticks out by 2 cm all round, whatever the size.
            m_Edge.localScale = new Vector3(1f + 0.04f / m_Width, 1f + 0.04f / height, 1f);
            PlaceCorners();
        }

        void BuildPanel()
        {
            // Six buttons, plus the browser's Type and the Moon cinema's Moon/Room: 104 dp each for six,
            // 90 for more, the panel as wide as they need.
            var moon = m_MoonAssets != null;
            var count = 6 + (m_IsBrowser ? 1 : 0) + (moon ? 1 : 0);
            var width = count > 6 ? 90f : 104f;
            var step = width + 12f;
            m_PanelWidth = Mathf.Max(700f, count * step + 24f);
            m_Panel = WorldUi.Panel("Window Panel", transform, new Vector2(m_PanelWidth, k_PanelHeight), k_PanelDistance);
            m_Status = M3.Text("Status", m_Panel, "", M3Theme.Type.BodyMedium, new Vector2(0f, 34f), new Vector2(m_PanelWidth - 120f, 24f),
                M3Theme.OnSurfaceVariant);
            var slot = 0;
            Vector2 Next() => new((slot++ - (count - 1) / 2f) * step, -20f);
            M3.Button("Back", m_Panel, "Back", M3Button.Variant.Tonal, Next(), () => m_Bridge.Key(k_KeyBack), width: width);
            M3.Button("Home", m_Panel, "Home", M3Button.Variant.Tonal, Next(), GoHome, width: width);
            if (m_IsBrowser)
                M3.Button("Type", m_Panel, "Type", M3Button.Variant.Tonal, Next(), ShowKeyboard, width: width);
            M3.Button("Scroll Up", m_Panel, "Up", M3Button.Variant.Tonal, Next(), () => Scroll(1), width: width);
            M3.Button("Scroll Down", m_Panel, "Down", M3Button.Variant.Tonal, Next(), () => Scroll(-1), width: width);
            M3.Button("Center", m_Panel, "Center", M3Button.Variant.Tonal, Next(), PlaceWindow, width: width);
            if (moon)
                m_MoonButton = M3.Button("Moon", m_Panel, "Moon", M3Button.Variant.Tonal, Next(), () => SetMoon(!m_OnMoon), width: width);
            M3.Button("Leave", m_Panel, "Leave", M3Button.Variant.Text, Next(), () => m_Core.Demos.ExitToLobby(), width: width);
            if (m_IsBrowser)
            {
                m_Keyboard = WindowKeyboard.Build(transform, k_PanelDistance);
                m_Keyboard.Go += url => m_Bridge.OpenUrl(url);
                m_Keyboard.Type += text =>
                {
                    m_Bridge.Text(text);
                    m_Bridge.Key(k_KeyEnter);
                };
                m_Keyboard.Closed += () => m_Panel.gameObject.SetActive(true);
            }
        }

        // The Moon cinema on or off. On: 6DoF with hands, passthrough off, and the Moon built around you, the window put back in front once the new head pose
        // has settled. Off: back to the room in 6DoF passthrough with hands, as the demo starts.
        // Leaving the demo puts back what was on before it (DemoManager), so nothing to undo there.
        void SetMoon(bool on)
        {
            if (on == m_OnMoon || m_MoonAssets == null)
                return;
            m_OnMoon = on;
            m_MoonButton.Text = on ? "Room" : "Moon";
            if (on)
            {
                m_Core.Apply(new CoreState(true, false, true));
                m_MoonRoot = new GameObject("Moon Cinema");
                m_MoonRoot.transform.SetParent(transform, false);
                var moon = m_MoonRoot.AddComponent<MoonWorld>();
                moon.Lite = true;
                moon.Build(m_MoonAssets, m_MoonRoot.transform, m_MoonRoot.transform);
            }
            else
            {
                Destroy(m_MoonRoot);
                m_MoonRoot = null;
                m_Core.Apply(new CoreState(true, true, true));
            }
            Debug.Log($"[Apps] Moon cinema {(on ? "on: 6DoF, passthrough off" : "off: back to the room")}");
            StartCoroutine(SettleAfterSwitch());
        }

        // The head's pose jumps when tracking switches between 3DoF and 6DoF: a few frames later, the
        // Moon goes under the eyes, facing where they look, and the window and panel in front.
        System.Collections.IEnumerator SettleAfterSwitch()
        {
            for (var i = 0; i < 5; i++)
                yield return null;
            if (m_MoonRoot != null)
            {
                var head = m_Core.Head;
                var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                var yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized) : Quaternion.identity;
                var floor = m_Core.Boundary != null ? m_Core.Boundary.FloorHeight : null;
                var ground = floor ?? head.position.y - k_MoonEyeHeight;
                m_MoonRoot.transform.SetPositionAndRotation(new Vector3(head.position.x, ground, head.position.z), yaw);
                Debug.Log($"[Apps] Moon ground {head.position.y - ground:F2} m below the eyes ({(floor.HasValue ? "the real floor" : "no floor found")})");
            }
            PlaceWindow();
        }

        // The app's start: YouTube's home, or the browser's home page.
        void GoHome()
        {
            if (m_IsBrowser)
                m_Bridge.OpenUrl(k_BrowserHome);
            else
                m_Bridge.Launch(m_App);
        }

        // The keyboard takes the panel's place, a little higher, until it's closed or used.
        void ShowKeyboard()
        {
            if (m_Keyboard == null)
                return;
            m_Panel.gameObject.SetActive(false);
            m_Keyboard.Show(m_Core.Head, k_PanelDistance, k_KeyboardPitch);
        }

        // Move and Close, each a round button on its own small panel, on the window's top corners.
        void BuildCorners()
        {
            m_MoveCorner = WorldUi.Panel("Move Corner", transform, k_CornerSize, k_WindowDistance, null, k_CornerSize.x / 2f);
            M3.IconButton("Move", m_MoveCorner, "open_with", M3Button.Variant.Tonal, Vector2.zero, StartMove, 56f);
            m_CloseCorner = WorldUi.Panel("Close Corner", transform, k_CornerSize, k_WindowDistance, null, k_CornerSize.x / 2f);
            M3.IconButton("Close", m_CloseCorner, "close", M3Button.Variant.Tonal, Vector2.zero, CloseApp, 56f);
            PlaceCorners();
        }

        void PlaceCorners()
        {
            if (m_Window == null)
                return;
            PlaceHandles();
            if (m_MoveCorner == null)
                return;
            // Side by side in a small bar centered above the window, clear of its edge. At the top
            // corners they sat beside the app's own menu and back buttons, and a tap meant for those
            // pressed Move.
            const float above = 0.13f;
            const float apart = 0.075f;
            var toward = -m_Window.forward * 0.02f;
            var top = m_Width * k_Height / k_Width / 2f + above;
            m_MoveCorner.SetPositionAndRotation(m_Window.position + m_Window.rotation * new Vector3(-apart, top, 0f) + toward, m_Window.rotation);
            m_CloseCorner.SetPositionAndRotation(m_Window.position + m_Window.rotation * new Vector3(apart, top, 0f) + toward, m_Window.rotation);
        }

        // The L handles: at each bottom corner a horizontal and a vertical bar, 3 cm outside the window.
        void PlaceHandles()
        {
            if (m_Handles[0] == null)
                return;
            const float length = 0.16f;
            const float thickness = 0.022f;
            const float gap = 0.03f;
            var half = new Vector2(m_Width / 2f, m_Width * k_Height / k_Width / 2f);
            for (var side = 0; side < 2; side++)
            {
                var sx = side == 0 ? -1f : 1f;
                var corner = new Vector2(sx * (half.x + gap), -(half.y + gap));
                var across = m_Handles[side * 2];
                var up = m_Handles[side * 2 + 1];
                across.SetPositionAndRotation(m_Window.position +
                    m_Window.rotation * new Vector3(corner.x - sx * (length / 2f - thickness / 2f), corner.y, -0.005f), m_Window.rotation);
                across.localScale = new Vector3(length, thickness, 1f);
                up.SetPositionAndRotation(m_Window.position +
                    m_Window.rotation * new Vector3(corner.x, corner.y + length / 2f - thickness / 2f, -0.005f), m_Window.rotation);
                up.localScale = new Vector3(thickness, length, 1f);
            }
        }

        // Ahead of the head at eye level, with the panel below it.
        void PlaceWindow()
        {
            var head = m_Core.Head;
            var flat = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            var yaw = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat.normalized) : Quaternion.identity;
            m_Window.SetPositionAndRotation(head.position + yaw * Vector3.forward * k_WindowDistance, yaw);
            WorldUi.PlaceInFront(m_Panel, head, k_PanelDistance, k_PanelPitch);
            PlaceCorners();
        }

        // Where the gaze looks, upright and facing the head: where Move puts the window.
        void PlaceWhereLooking()
        {
            var head = m_Core.Head;
            var position = head.position + head.forward * k_WindowDistance;
            var facing = Vector3.ProjectOnPlane(position - head.position, Vector3.up);
            var rotation = facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing.normalized) : m_Window.rotation;
            m_Window.SetPositionAndRotation(position, rotation);
            PlaceCorners();
        }

        // Move, as in Android XR: the window stays in view and follows the gaze until a pinch puts it
        // down. (It used to hide until the next pinch, so a Move pressed by mistake turned the next
        // tap into moving the window, with nothing to show it.)
        void StartMove()
        {
            m_Moving = true;
            m_Status.text = "Look where you want the window, then pinch to put it there.";
            Debug.Log("[Apps] Moving the window: it follows the gaze until a pinch");
        }

        void FinishMove()
        {
            m_Moving = false;
            PlaceWhereLooking();
            m_Status.text = Describe(m_State);
            Debug.Log("[Apps] Window moved");
        }

        void CloseApp()
        {
            Debug.Log("[Apps] Closed from the window's corner");
            m_Core.Demos.ExitToLobby();
        }

        // Moves by half the window's height: direction +1 shows what's above, -1 what's below.
        void Scroll(int direction)
        {
            // A mouse wheel, not a swipe: a swipe touched the display, which took focus from Unity and
            // froze the stereo view (passthrough stalled, then black) for about a second.
            m_Bridge.Scroll(k_Width / 2, k_Height / 2, k_ScrollNotches * direction, k_Height / 4 * direction);
        }

        // The app on the window can take the phone's focus (opening a link, a new screen), and Unity
        // stops drawing without it. Asked back only then: giving it back after every tap restarted
        // Unity's activity each time, and ARCore lost tracking and recentred the room.
        void OnApplicationFocus(bool focused)
        {
            if (focused || m_State != AppWindowState.Running)
                return;
            Debug.Log("[Apps] Lost the phone's focus to the window's app; taking it back");
            m_Bridge.Focus();
        }

        void OnApplicationPause(bool paused) => m_Bridge.Paused(paused);

        // Two fingers out, palm away, moved up or down with the gaze on the window: scrolls it there
        // (TwoFingerScroll), as wheel notches.
        void FollowTwoFingers()
        {
            var tracker = m_Core.HandTracker;
            var seen = tracker != null && tracker.isActiveAndEnabled && tracker.IsTracking && m_Gesture == Gesture.None && !m_Moving;
            var landmarks = seen ? tracker.ImageLandmarks : null;
            var pose = false;
            var local = Vector3.zero;
            if (landmarks != null && landmarks.Count >= 21 && GazeOnWindow(out local))
            {
                var facing = PalmGesture.Facing(landmarks[PalmGesture.k_Wrist], landmarks[PalmGesture.k_IndexKnuckle],
                    landmarks[PalmGesture.k_PinkyKnuckle], tracker.HandednessScore >= 0.5f);
                pose = TwoFingerScroll.IsPose(tracker.Joints, facing);
            }
            var height = seen ? tracker.Joints[9].y : 0f;
            var notches = m_TwoFingers.Update(pose, height, Time.unscaledDeltaTime);
            if (m_TwoFingers.IsScrolling || m_TwoFingers.IsArming)
                m_TwoFingersAt = Time.unscaledTime;
            if (notches != 0)
            {
                m_PendingNotches += notches;
                m_PendingScrollAt = PixelAt(local, k_Width, k_Height);
            }
            if (m_PendingNotches == 0 || Time.unscaledTime < m_NextScrollSend)
                return;
            m_NextScrollSend = Time.unscaledTime + k_ScrollSendSeconds;
            m_Bridge.Scroll(m_PendingScrollAt.x, m_PendingScrollAt.y, m_PendingNotches, m_PendingNotches * 40);
            m_PendingNotches = 0;
        }

        // How curled each finger is: tip to the palm's middle, in palm lengths, for the index, middle,
        // ring and little fingers. False if the hand isn't tracked.
        bool FingerRatios(float[] ratios)
        {
            var tracker = m_Core.HandTracker;
            if (tracker == null || !tracker.isActiveAndEnabled || !tracker.IsTracking)
                return false;
            var j = tracker.Joints;
            var palm = (j[0] + j[5] + j[9] + j[13] + j[17]) / 5f;
            var length = Vector3.Distance(j[0], j[9]);
            if (length < 1e-3f)
                return false;
            ratios[0] = Vector3.Distance(j[8], palm) / length;
            ratios[1] = Vector3.Distance(j[12], palm) / length;
            ratios[2] = Vector3.Distance(j[16], palm) / length;
            ratios[3] = Vector3.Distance(j[20], palm) / length;
            return true;
        }

        void FollowFist()
        {
            if (m_Moving || m_Gesture == Gesture.Resize || m_TwoFingers.IsScrolling)
                return;
            var tracked = FingerRatios(m_Ratios);
            var curled = 0;
            var straight = 0;
            if (tracked)
            {
                m_LostSince = -1f;
                foreach (var ratio in m_Ratios)
                {
                    if (ratio < k_CurledRatio)
                        curled++;
                    if (ratio > k_StraightRatio)
                        straight++;
                }
            }
            var fist = tracked && curled == 4;
            var open = tracked && straight >= 3;
            var dt = Time.unscaledDeltaTime;
            m_FistFor = fist ? m_FistFor + dt : 0f;
            m_OpenFor = open ? m_OpenFor + dt : 0f;
            var hand = tracked ? (m_Core.HandTracker.Joints[0] + m_Core.HandTracker.Joints[9]) / 2f : Vector3.zero;

            if (m_Gesture != Gesture.Grab)
            {
                if (m_FistFor < k_HandShapeSeconds || !GazeOnWindow(out _))
                    return;
                m_Gesture = Gesture.Grab;
                m_GrabHand = hand;
                m_GrabWindow = m_Window.position;
                m_GrabPanel = m_Panel.position;
                m_TwoFingersAt = Time.unscaledTime;
                Debug.Log($"[Apps] Grabbed the window (fingers {m_Ratios[0]:F2} {m_Ratios[1]:F2} {m_Ratios[2]:F2} {m_Ratios[3]:F2} palm lengths)");
                return;
            }

            // Grabbed: taps stay ignored until a moment after it's let go.
            m_TwoFingersAt = Time.unscaledTime;
            if (open && m_OpenFor >= k_HandShapeSeconds || !tracked && LostTooLong())
            {
                m_Gesture = Gesture.None;
                m_LostSince = -1f;
                Debug.Log(tracked ? "[Apps] Let go of the window" : "[Apps] Lost the hand: the window stays there");
                return;
            }
            if (!tracked)
                return;
            // The window follows the hand's movement, scaled up, kept at its distance from the head,
            // upright and facing it; the panel moves with it.
            var head = m_Core.Head.position;
            var target = m_GrabWindow + (hand - m_GrabHand) * k_GrabReach;
            var distance = Vector3.Distance(head, m_GrabWindow);
            var position = head + (target - head).normalized * distance;
            var facing = Vector3.ProjectOnPlane(position - head, Vector3.up);
            var rotation = facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing.normalized) : m_Window.rotation;
            m_Window.SetPositionAndRotation(position, rotation);
            m_Panel.position = m_GrabPanel + (position - m_GrabWindow);
            m_Panel.rotation = Quaternion.LookRotation(m_Panel.position - head, Vector3.up);
            PlaceCorners();
        }

        // True once the hand has been out of view half a second while grabbing.
        bool LostTooLong()
        {
            if (m_LostSince < 0f)
                m_LostSince = Time.unscaledTime;
            return Time.unscaledTime - m_LostSince > 0.5f;
        }

        // Where the gaze meets the window, in its local space (-0.5 to 0.5 across and up), if it does.
        bool GazeOnWindow(out Vector3 local)
        {
            local = default;
            if (m_Window == null || !m_Window.gameObject.activeInHierarchy)
                return false;
            var ray = new Ray(m_Core.Head.position, m_Core.Head.forward);
            var plane = new Plane(m_Window.forward, m_Window.position);
            if (!plane.Raycast(ray, out var distance))
                return false;
            local = m_Window.InverseTransformPoint(ray.GetPoint(distance));
            var height = m_Width * k_Height / k_Width;
            return Mathf.Abs(local.x) <= 0.5f + k_HandleReach / m_Width && Mathf.Abs(local.y) <= 0.5f + k_HandleReach / height;
        }

        // Whether a point (window-local) is on the window itself, not the band around it.
        static bool OnWindow(Vector3 local) => Mathf.Abs(local.x) <= 0.5f && Mathf.Abs(local.y) <= 0.5f;

        // Which bottom corner's handle the gaze is on, as (-1 or +1, -1); zero for none. Only the
        // handles resize: within k_HandleReach of a bottom corner, outside the window.
        Vector2 EdgeAt(Vector3 local)
        {
            if (OnWindow(local))
                return Vector2.zero;
            var height = m_Width * k_Height / k_Width;
            var point = new Vector2(local.x * m_Width, local.y * height);
            var corner = new Vector2(Mathf.Sign(local.x) * m_Width / 2f, -height / 2f);
            return (point - corner).magnitude < k_HandleReach ? new Vector2(Mathf.Sign(local.x), -1f) : Vector2.zero;
        }

        void ShowEdge()
        {
            var edge = m_Gesture == Gesture.Resize ? m_ResizeSide
                : m_Gesture == Gesture.None && GazeOnWindow(out var local) ? EdgeAt(local) : Vector2.zero;
            // The frame round the window lights while something is happening to the window as a whole
            // (scrolling, moving, resizing), dimly while the scroll pose is about to start, and is
            // otherwise off: lit by the gaze alone it looked like a resize was coming.
            var active = m_TwoFingers.IsScrolling || m_Moving || m_Gesture == Gesture.Resize || m_Gesture == Gesture.Grab;
            var ready = !active && m_TwoFingers.IsArming;
            if (m_Edge.gameObject.activeSelf != (active || ready))
                m_Edge.gameObject.SetActive(active || ready);
            if (active || ready)
                m_EdgeMaterial.color = active ? k_EdgeActive : k_EdgeReady;
            // The handles light up on their corner.
            var corner = edge.y < 0f && edge.x != 0f;
            m_HandleMaterial.color = corner ? new Color(0.55f, 0.8f, 1f) : new Color(0.75f, 0.78f, 0.82f);
        }

        // A pinch begins: on the window's edge it resizes, on the window it touches (then taps or
        // drags), and while moving the window it puts it down. Otherwise the pinch goes on as usual.
        public bool TryBeginPinch(GameObject gazeTarget, PinchSource source)
        {
            if (m_Moving)
            {
                FinishMove();
                return true;
            }
            // A fist holding the window reads as a pinch too: it's still the grab.
            if (m_Gesture == Gesture.Grab)
                return true;
            // Only a resize handle: everything on the window is the ordinary gaze click, a tap.
            if (source != PinchSource.Hand || !GazeOnWindow(out var local))
                return false;
            if (gazeTarget != null)
                return false;
            var edge = EdgeAt(local);
            if (edge == Vector2.zero)
                return false;
            var tracker = m_Core.HandTracker;
            if (tracker == null || !tracker.IsTracking)
                return false;
            m_PinchStart = tracker.PinchPoint;
            m_Gesture = Gesture.Resize;
            m_ResizeSide = edge;
            m_ResizeFrom = m_Width;
            Debug.Log($"[Apps] Resizing from the bottom {(edge.x > 0 ? "right" : "left")} corner");
            return true;
        }

        public void EndPinch()
        {
            if (m_Gesture == Gesture.Grab)
                return;
            if (m_Gesture == Gesture.Resize)
                Debug.Log($"[Apps] Window resized to {m_Width:F2} m wide");
            m_Gesture = Gesture.None;
        }

        // The hand's movement since the pinch began, on the window's plane, in the window's meters.
        Vector2 HandMoved()
        {
            var tracker = m_Core.HandTracker;
            var moved = tracker.PinchPoint - m_PinchStart;
            var handDistance = Mathf.Max(0.15f, Vector3.Distance(m_Core.Head.position, tracker.PinchPoint));
            var windowDistance = Vector3.Distance(m_Core.Head.position, m_Window.position);
            var reach = Mathf.Clamp(windowDistance / handDistance, 1f, k_MaxReach);
            return new Vector2(Vector3.Dot(moved, m_Window.right), Vector3.Dot(moved, m_Window.up)) * reach;
        }

        void FollowPinch()
        {
            if (m_Gesture == Gesture.None)
                return;
            var tracker = m_Core.HandTracker;
            if (tracker == null || !tracker.IsTracking)
                return;
            var moved = HandMoved();
            switch (m_Gesture)
            {
                case Gesture.Resize:
                    // The edge follows the hand outward or inward, the opposite edge staying put.
                    // A side edge grows by the hand's movement across, a top or bottom edge by its
                    // movement up or down (in width, at 16:9), a corner by whichever is more.
                    var across = moved.x * m_ResizeSide.x;
                    var up = moved.y * m_ResizeSide.y * k_Width / k_Height;
                    var grow = m_ResizeSide.x == 0f ? up : m_ResizeSide.y == 0f ? across : Mathf.Max(across, up);
                    var width = Mathf.Clamp(m_ResizeFrom + grow, k_MinWidth, k_MaxWidth);
                    if (Mathf.Abs(width - m_Width) < 1e-4f)
                        break;
                    var change = width - m_Width;
                    var shift = new Vector3(m_ResizeSide.x * change / 2f, m_ResizeSide.y * change * k_Height / k_Width / 2f, 0f);
                    m_Window.position += m_Window.rotation * shift;
                    m_Width = width;
                    ApplySize();
                    break;
            }
        }

        // A pinch or a screen tap with the gaze on the window: a tap in the app where the gaze is.
        void OnWindowClicked()
        {
            if (!GazeOnWindow(out var local))
                return;
            if (Time.unscaledTime - m_TwoFingersAt < k_ScrollTapGuard)
            {
                Debug.Log("[Apps] Tap ignored: it came with the two-finger scroll or a grab");
                return;
            }
            var pixel = PixelAt(local, k_Width, k_Height);
            StartRipple(local);
            Debug.Log($"[Apps] Tap at {pixel.x}, {pixel.y}");
            m_Bridge.Tap(pixel.x, pixel.y);
        }

        void StartRipple(Vector3 local)
        {
            if (m_Ripple == null)
            {
                var shader = Shader.Find("CardboardXR/Reticle");
                if (shader == null)
                    return;
                m_RippleMaterial = new Material(shader) { name = "Tap Ripple" };
                m_RippleMaterial.SetFloat("_Outline", 0f);
                var ripple = GameObject.CreatePrimitive(PrimitiveType.Quad);
                ripple.name = "Tap Ripple";
                Destroy(ripple.GetComponent<Collider>());
                ripple.transform.SetParent(transform, false);
                ripple.GetComponent<MeshRenderer>().sharedMaterial = m_RippleMaterial;
                m_Ripple = ripple.transform;
            }
            // Just in front of the window, at the tapped point, at a fixed size whatever the window's.
            var point = m_Window.TransformPoint(new Vector3(local.x, local.y, 0f)) - m_Window.forward * 0.01f;
            m_Ripple.SetPositionAndRotation(point, m_Window.rotation);
            m_Ripple.localScale = new Vector3(k_RippleMeters, k_RippleMeters, 1f);
            m_RippleAt = Time.unscaledTime;
            m_Ripple.gameObject.SetActive(true);
        }

        void ShowRipple()
        {
            if (m_Ripple == null || !m_Ripple.gameObject.activeSelf)
                return;
            var t = (Time.unscaledTime - m_RippleAt) / k_RippleSeconds;
            if (t >= 1f)
            {
                m_Ripple.gameObject.SetActive(false);
                return;
            }
            // A ring that widens and thins as it fades.
            var eased = 1f - (1f - t) * (1f - t);
            var outer = Mathf.Lerp(0.25f, 1f, eased);
            m_RippleMaterial.SetFloat(k_RippleOuter, outer);
            m_RippleMaterial.SetFloat(k_RippleInner, outer * Mathf.Lerp(0.55f, 0.85f, eased));
            m_RippleMaterial.SetColor(k_RippleColor, new Color(1f, 1f, 1f, 0.85f * (1f - t)));
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
