using System;
using CardboardXR.Hands;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace CardboardXR.Demos.Apps
{
    // A floating window onto a real Android app: the app runs on a private virtual display, its picture
    // comes here through AppWindowBridge, and pinches on the window become touches in the app:
    //   a pinch and release in place taps there;
    //   a pinch, then moving the hand while pinched, drags the app's content with the hand (scrolling a
    //   feed, say), sent as swipes since the bridge's input is one command per gesture: one when the
    //   fingers open, and more along the way on a long drag;
    //   a pinch on the window's edge (the outer k_EdgeMeters, which light up under the gaze) resizes the
    //   window, pulling that edge or corner with the hand, the app's picture staying 16:9.
    // Two round buttons sit on the window's top corners: Move (left) hides the window, and the next
    // pinch puts it back where you're looking; Close (right) closes the app and leaves. The panel below
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
        // The band along the window's edge that resizes, in meters on the window.
        const float k_EdgeMeters = 0.12f;
        // A hand that moves less than this while pinched taps; more drags, in meters.
        const float k_DragStart = 0.015f;
        // A long drag sends a swipe each time it has gone this many app pixels.
        const int k_DragChunk = 220;
        // How much farther the window is than the hand, for turning the hand's movement into the
        // window's: the hand moves a little to drag across a window two meters away.
        const float k_HandReach = 0.45f;
        static readonly Vector2 k_PanelSize = new(700f, 112f);
        static readonly Vector2 k_CornerSize = new(64f, 64f);

        [Tooltip("An unlit material for the app's picture. Copied at runtime.")]
        [SerializeField] Material m_Material;

        enum Gesture
        {
            None,
            Touch,
            Drag,
            Resize,
        }

        CoreServices m_Core;
        readonly AppWindowBridge m_Bridge = new();
        Transform m_Window;
        Transform m_Edge;
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
        FloatingWindows.Window m_WindowEntry;
        FloatingWindows.Window m_PanelEntry;

        // The pinch held on the window, if any.
        Gesture m_Gesture;
        Vector3 m_PinchStart;
        Vector2Int m_TouchPixel;
        Vector2 m_DragSent;
        Vector2 m_ResizeSide;
        float m_ResizeFrom;

        public override void OnDemoEnter(CoreServices core)
        {
            m_Core = core;
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
            m_Bridge.Start(k_Width, k_Height, k_Dpi, k_App);
        }

        public override void OnDemoExit(CoreServices core)
        {
            m_Bridge.Stop();
            FloatingWindows.Unregister(m_WindowEntry);
            FloatingWindows.Unregister(m_PanelEntry);
            Destroy(m_Texture);
            Destroy(m_WindowMaterial);
            Destroy(m_EdgeMaterial);
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
            if (m_MoveCorner == null || m_Window == null)
                return;
            // Just outside the top corners, a little in front of the window.
            var outward = 0.06f;
            var toward = -m_Window.forward * 0.02f;
            var half = new Vector2(m_Width / 2f, m_Width * k_Height / k_Width / 2f);
            m_MoveCorner.SetPositionAndRotation(m_Window.position + m_Window.rotation * new Vector3(-half.x - outward, half.y + outward, 0f) + toward,
                m_Window.rotation);
            m_CloseCorner.SetPositionAndRotation(m_Window.position + m_Window.rotation * new Vector3(half.x + outward, half.y + outward, 0f) + toward,
                m_Window.rotation);
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

        void StartMove()
        {
            m_Moving = true;
            m_Window.gameObject.SetActive(false);
            m_MoveCorner.gameObject.SetActive(false);
            m_CloseCorner.gameObject.SetActive(false);
            m_Status.text = "Look where you want the window and pinch.";
            Debug.Log("[Apps] Moving the window: the next pinch puts it where the gaze is");
        }

        void FinishMove()
        {
            m_Moving = false;
            PlaceWhereLooking();
            m_Window.gameObject.SetActive(true);
            m_MoveCorner.gameObject.SetActive(true);
            m_CloseCorner.gameObject.SetActive(true);
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
            var margin = k_EdgeMeters / m_Width;
            return Mathf.Abs(local.x) <= 0.5f + margin && Mathf.Abs(local.y) <= 0.5f + margin * k_Width / k_Height;
        }

        // Which edges the gaze is on, as -1, 0 or +1 across and up; zero for neither.
        Vector2 EdgeAt(Vector3 local)
        {
            var height = m_Width * k_Height / k_Width;
            var x = Mathf.Abs(local.x) * m_Width > m_Width / 2f - k_EdgeMeters ? Mathf.Sign(local.x) : 0f;
            var y = Mathf.Abs(local.y) * height > height / 2f - k_EdgeMeters ? Mathf.Sign(local.y) : 0f;
            return new Vector2(x, y);
        }

        void ShowEdge()
        {
            var on = m_Gesture == Gesture.Resize || (m_Gesture == Gesture.None && GazeOnWindow(out var local) && EdgeAt(local) != Vector2.zero);
            if (m_Edge.gameObject.activeSelf != on)
                m_Edge.gameObject.SetActive(on);
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
            if (source != PinchSource.Hand || !GazeOnWindow(out var local))
                return false;
            var tracker = m_Core.HandTracker;
            if (tracker == null || !tracker.IsTracking)
                return false;
            m_PinchStart = tracker.PinchPoint;
            var edge = EdgeAt(local);
            if (edge != Vector2.zero)
            {
                m_Gesture = Gesture.Resize;
                m_ResizeSide = edge;
                m_ResizeFrom = m_Width;
                Debug.Log($"[Apps] Resizing from the {(edge.y > 0 ? "top" : edge.y < 0 ? "bottom" : "")}{(edge.x > 0 ? " right" : edge.x < 0 ? " left" : "")} edge");
                return true;
            }
            m_Gesture = Gesture.Touch;
            m_TouchPixel = PixelAt(local, k_Width, k_Height);
            m_DragSent = Vector2.zero;
            return true;
        }

        public void EndPinch()
        {
            switch (m_Gesture)
            {
                case Gesture.Touch:
                    Debug.Log($"[Apps] Tap at {m_TouchPixel.x}, {m_TouchPixel.y}");
                    m_Bridge.Tap(m_TouchPixel.x, m_TouchPixel.y);
                    break;
                case Gesture.Drag:
                    SendDrag(true);
                    break;
                case Gesture.Resize:
                    Debug.Log($"[Apps] Window resized to {m_Width:F2} m wide");
                    break;
            }
            m_Gesture = Gesture.None;
        }

        // The hand's movement since the pinch began, on the window's plane, in the window's meters.
        Vector2 HandMoved()
        {
            var tracker = m_Core.HandTracker;
            var moved = tracker.PinchPoint - m_PinchStart;
            var handDistance = Mathf.Max(0.15f, Vector3.Distance(m_Core.Head.position, tracker.PinchPoint));
            var windowDistance = Vector3.Distance(m_Core.Head.position, m_Window.position);
            var reach = Mathf.Clamp(windowDistance / handDistance, 1f, windowDistance / k_HandReach * 1.5f);
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
                case Gesture.Touch:
                    if ((tracker.PinchPoint - m_PinchStart).magnitude > k_DragStart)
                    {
                        m_Gesture = Gesture.Drag;
                        Debug.Log("[Apps] Dragging");
                    }
                    break;
                case Gesture.Drag:
                    SendDrag(false);
                    break;
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

        // The app's content follows the hand: swipe from where the pinch touched by the hand's
        // movement, in app pixels. Mid-drag, only once it has gone k_DragChunk past the last swipe.
        void SendDrag(bool final)
        {
            var moved = HandMoved();
            var pixels = new Vector2(moved.x / m_Width * k_Width, -moved.y / (m_Width * k_Height / k_Width) * k_Height);
            var step = pixels - m_DragSent;
            if (!final && step.magnitude < k_DragChunk)
                return;
            if (step.magnitude < 4f)
                return;
            var from = m_TouchPixel + Vector2Int.RoundToInt(m_DragSent);
            var to = m_TouchPixel + Vector2Int.RoundToInt(pixels);
            from = Clamp(from);
            to = Clamp(to);
            Debug.Log($"[Apps] Drag {from.x}, {from.y} to {to.x}, {to.y}{(final ? "" : " (on the way)")}");
            m_Bridge.Swipe(from.x, from.y, to.x, to.y, k_SwipeMilliseconds);
            m_DragSent = pixels;
        }

        static Vector2Int Clamp(Vector2Int pixel) =>
            new(Mathf.Clamp(pixel.x, 0, k_Width - 1), Mathf.Clamp(pixel.y, 0, k_Height - 1));

        // A screen tap, or a 3DoF pinch: no hand position, so a plain tap where the gaze is.
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
