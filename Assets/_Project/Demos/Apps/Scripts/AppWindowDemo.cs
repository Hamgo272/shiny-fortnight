using System;
using CardboardXR.Hands;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

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
    public class AppWindowDemo : Demo, IPinchHandler
    {
        const string k_App = "com.google.android.youtube";
        const int k_Width = 1280;
        const int k_Height = 720;
        const int k_Dpi = 200;
        const int k_KeyBack = 4;
        const int k_SwipeMilliseconds = 250;

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
        static readonly Vector2 k_PanelSize = new(700f, 112f);
        static readonly Vector2 k_CornerSize = new(64f, 64f);

        [Tooltip("An unlit material for the app's picture. Copied at runtime.")]
        [SerializeField] Material m_Material;

        enum Gesture
        {
            None,
            Resize,
        }

        CoreServices m_Core;
        readonly AppWindowBridge m_Bridge = new();
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
            m_PanelEntry = FloatingWindows.Register("Window Panel", m_Panel, M3Theme.DpToMeters(k_PanelSize.x, k_PanelDistance), true,
                k_PanelDistance);
            PinchRouter.Handler = this;
            m_SavedFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = k_FrameRate;
            Debug.Log($"[Apps] To keep cool: {k_FrameRate} fps");
            m_Bridge.Start(k_Width, k_Height, k_Dpi, k_App);
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
            ShowEdge();
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
            var distance = k_Height / 4 * direction;
            m_Bridge.Swipe(k_Width / 2, k_Height / 2 - distance, k_Width / 2, k_Height / 2 + distance, k_SwipeMilliseconds);
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
            // The frame round the window stays off: only the handles show a resize is coming.
            if (m_Edge.gameObject.activeSelf)
                m_Edge.gameObject.SetActive(false);
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
            var pixel = PixelAt(local, k_Width, k_Height);
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
