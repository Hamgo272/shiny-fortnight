using System;
using CardboardXR.Hands;
using CardboardXR.UI;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

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
    // Two round buttons sit in a small bar above the window: Move (left) hides the window, and the
    // next pinch puts it where the gaze is (following the gaze instead blocked the view); Close
    // (right) closes the app and leaves. A pinch while looking at a detected wall puts the window on
    // the wall, as big as the wall allows up to k_MaxWidth, like a cinema screen.
    // A fist grabs the window, as in Android XR's grab: look at the window and close the hand (palm
    // outward is best: the camera sees the back of the hand, and the settings gear, which needs the
    // palm toward you, can't come up). The window, outlined, follows the hand round you at its
    // distance, the hand's movement scaled up (k_GrabReach); open the hand to drop it, on a wall if
    // it's dropped on one. A fist closing can pass through a pinch (the thumb folds over the index
    // finger), so a tap on the window waits k_TapDelay and is cancelled if a fist forms meanwhile.
    // The window and its panel are anchored to the room with an ARCore anchor, made again each time
    // they're placed (Move, a resize, Arrange, Center): unanchored, they moved against the room
    // whenever ARCore corrected its tracking, which looked like the window jumping about. The panel below
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
            Grab,
        }

        // A finger is curled when its tip is nearer the palm's middle than this many palm lengths
        // (wrist to the middle finger's knuckle), and straight beyond k_StraightRatio.
        const float k_CurledRatio = 0.8f;
        const float k_StraightRatio = 1.3f;
        // How long a fist (or an open hand, to drop) must hold, in seconds, to count.
        const float k_HandShapeSeconds = 0.08f;
        // A tap waits this long, in seconds, in case a fist is forming.
        const float k_TapDelay = 0.15f;
        // The hand's movement while grabbing is scaled up this much on the window.
        const float k_GrabReach = 3f;

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
        // The fist grab: how long the hand has looked like a fist (or open), where the hand and the
        // window were when it closed, and a tap waiting in case a fist is forming.
        float m_FistFor;
        float m_OpenFor;
        Vector3 m_GrabHand;
        Vector3 m_GrabWindow;
        Vector3 m_GrabPanel;
        Vector2Int? m_PendingTap;
        float m_PendingTapAt;
        // The anchor holding the window and the panel to the room, and where they sit relative to it.
        ARAnchor m_Anchor;
        Pose m_WindowOnAnchor;
        Pose m_PanelOnAnchor;
        int m_AnchorRequest;
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
                    ShowWindow(true);
                m_Moving = false;
                m_Window.SetPositionAndRotation(pose.position, pose.rotation);
                PlaceCorners();
                Reanchor();
            }, false, k_WindowDistance);
            m_PanelEntry = FloatingWindows.Register("Window Panel", () => M3Theme.DpToMeters(k_PanelSize.x, k_PanelDistance), pose =>
            {
                m_Panel.SetPositionAndRotation(pose.position, pose.rotation);
                Reanchor();
            }, true, k_PanelDistance);
            PinchRouter.Handler = this;
            m_SavedFrameRate = Application.targetFrameRate;
            Application.targetFrameRate = k_FrameRate;
            Debug.Log($"[Apps] To keep cool: {k_FrameRate} fps");
            m_Bridge.Start(k_Width, k_Height, k_Dpi, k_App);
        }

        public override void OnDemoExit(CoreServices core)
        {
            m_Bridge.Stop();
            core.Placer.RemoveAnchor(m_Anchor);
            m_Anchor = null;
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
            FollowPinch();
            FollowFist();
            SendPendingTap();
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
            Reanchor();
        }

        // Anchors the window and the panel where they are now, replacing any anchor they had. Until
        // ARCore adds it they stay as placed; without ARCore (3DoF) they just aren't anchored.
        void Reanchor()
        {
            m_Core.Placer.RemoveAnchor(m_Anchor);
            m_Anchor = null;
            var request = ++m_AnchorRequest;
            var pose = new Pose(m_Window.position, m_Window.rotation);
            m_Core.Placer.AddAnchor(pose, anchor =>
            {
                if (this == null || request != m_AnchorRequest)
                {
                    if (m_Core != null)
                        m_Core.Placer.RemoveAnchor(anchor);
                    return;
                }
                m_Anchor = anchor;
                if (anchor == null)
                    return;
                var inverse = Quaternion.Inverse(anchor.transform.rotation);
                m_WindowOnAnchor = new Pose(inverse * (m_Window.position - anchor.transform.position), inverse * m_Window.rotation);
                m_PanelOnAnchor = new Pose(inverse * (m_Panel.position - anchor.transform.position), inverse * m_Panel.rotation);
            });
        }

        // The window and the panel follow their anchor as ARCore refines it, except while the window is
        // being resized (it's re-anchored after).
        void LateUpdate()
        {
            if (m_Anchor == null || m_Gesture != Gesture.None || m_Moving)
                return;
            var anchor = m_Anchor.transform;
            m_Window.SetPositionAndRotation(anchor.position + anchor.rotation * m_WindowOnAnchor.position, anchor.rotation * m_WindowOnAnchor.rotation);
            m_Panel.SetPositionAndRotation(anchor.position + anchor.rotation * m_PanelOnAnchor.position, anchor.rotation * m_PanelOnAnchor.rotation);
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

        // Move: the window hides, and the next pinch puts it where the gaze is, or on the wall there.
        void StartMove()
        {
            m_Moving = true;
            ShowWindow(false);
            m_Status.text = "Look where you want the window (or at a wall) and pinch.";
            Debug.Log("[Apps] Moving the window: the next pinch puts it where the gaze is");
        }

        void FinishMove()
        {
            m_Moving = false;
            if (!PlaceOnWall())
            {
                PlaceWhereLooking();
                Debug.Log("[Apps] Window moved");
            }
            ShowWindow(true);
            m_Status.text = Describe(m_State);
            Reanchor();
        }

        void ShowWindow(bool show)
        {
            m_Window.gameObject.SetActive(show);
            foreach (var handle in m_Handles)
                handle.gameObject.SetActive(show);
            m_MoveCorner.gameObject.SetActive(show);
            m_CloseCorner.gameObject.SetActive(show);
        }

        // If the gaze is on a detected wall, puts the window flat on it, a little off it, as wide as the
        // wall allows (16:9, up to k_MaxWidth), centered where the gaze meets it: a cinema screen.
        bool PlaceOnWall() => PlaceOnWall(new Ray(m_Core.Head.position, m_Core.Head.forward));

        // The same along any ray from the head: through the window's middle, for a grab dropped there.
        bool PlaceOnWall(Ray ray)
        {
            var head = m_Core.Head;
            var hits = Physics.RaycastAll(ray, 10f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                var plane = hit.collider.GetComponentInParent<ARPlane>();
                if (plane == null)
                    continue;
                if (plane.alignment != PlaneAlignment.Vertical)
                    return false;
                var normal = Vector3.ProjectOnPlane(plane.normal, Vector3.up).normalized;
                if (Vector3.Dot(normal, head.position - hit.point) < 0f)
                    normal = -normal;
                var fit = Mathf.Min(plane.size.x, plane.size.y * k_Width / k_Height) * 0.92f;
                m_Width = Mathf.Clamp(fit, k_MinWidth, k_MaxWidth);
                ApplySize();
                m_Window.SetPositionAndRotation(hit.point + normal * 0.03f, Quaternion.LookRotation(-normal, Vector3.up));
                PlaceCorners();
                Debug.Log($"[Apps] Window on a wall {hit.distance:F1} m away, {m_Width:F2} m wide (wall {plane.size.x:F1} x {plane.size.y:F1} m)");
                return true;
            }
            return false;
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
            // The frame round the window shows only while it's grabbed: the handles show a resize.
            var grabbed = m_Gesture == Gesture.Grab;
            if (m_Edge.gameObject.activeSelf != grabbed)
                m_Edge.gameObject.SetActive(grabbed);
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
            // A fist holding the window can read as a pinch too: it's still the grab.
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
            var resized = m_Gesture == Gesture.Resize;
            m_Gesture = Gesture.None;
            if (resized)
            {
                Debug.Log($"[Apps] Window resized to {m_Width:F2} m wide");
                Reanchor();
            }
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
            // Held a moment: a fist forming can look like a pinch on the way (see FollowFist).
            m_PendingTap = PixelAt(local, k_Width, k_Height);
            m_PendingTapAt = Time.unscaledTime;
        }

        void SendPendingTap()
        {
            if (!m_PendingTap.HasValue || Time.unscaledTime - m_PendingTapAt < k_TapDelay)
                return;
            var pixel = m_PendingTap.Value;
            m_PendingTap = null;
            Debug.Log($"[Apps] Tap at {pixel.x}, {pixel.y}");
            m_Bridge.Tap(pixel.x, pixel.y);
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

        readonly float[] m_Ratios = new float[4];

        // The fist grab: a fist with the gaze on the window grabs it, the window follows the hand, and
        // an open hand drops it.
        void FollowFist()
        {
            if (m_Moving || m_Gesture == Gesture.Resize)
                return;
            var tracked = FingerRatios(m_Ratios);
            var curled = 0;
            var straight = 0;
            if (tracked)
            {
                foreach (var ratio in m_Ratios)
                {
                    if (ratio < k_CurledRatio)
                        curled++;
                    if (ratio > k_StraightRatio)
                        straight++;
                }
            }
            if (tracked)
                m_LostSince = -1f;
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
                if (m_PendingTap.HasValue)
                {
                    Debug.Log("[Apps] A fist formed: the tap just before it wasn't one");
                    m_PendingTap = null;
                }
                m_Gesture = Gesture.Grab;
                m_GrabHand = hand;
                m_GrabWindow = m_Window.position;
                m_GrabPanel = m_Panel.position;
                Debug.Log($"[Apps] Grabbed the window with a fist (pinch ratio {m_Core.HandTracker.PinchRatio:F2}, fingers " +
                          $"{m_Ratios[0]:F2} {m_Ratios[1]:F2} {m_Ratios[2]:F2} {m_Ratios[3]:F2} palm lengths)");
                return;
            }

            if (open && m_OpenFor >= k_HandShapeSeconds || !tracked && m_FistFor == 0f && m_OpenFor == 0f && LostTooLong())
            {
                DropGrab();
                return;
            }
            if (!fist && !tracked)
                return;
            // The window follows the hand's movement, scaled up, kept at its distance from the head
            // and turned to face it, upright.
            var head = m_Core.Head.position;
            var moved = (hand - m_GrabHand) * k_GrabReach;
            var target = m_GrabWindow + moved;
            var distance = Vector3.Distance(head, m_GrabWindow);
            var direction = (target - head).normalized;
            var position = head + direction * distance;
            var facing = Vector3.ProjectOnPlane(position - head, Vector3.up);
            var rotation = facing.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(facing.normalized) : m_Window.rotation;
            m_Window.SetPositionAndRotation(position, rotation);
            m_Panel.position = m_GrabPanel + (position - m_GrabWindow);
            m_Panel.rotation = Quaternion.LookRotation(m_Panel.position - head, Vector3.up);
            PlaceCorners();
        }

        float m_LostSince = -1f;

        // True once the hand has been out of view for a moment while grabbing: the window stays there.
        bool LostTooLong()
        {
            if (m_LostSince < 0f)
                m_LostSince = Time.unscaledTime;
            return Time.unscaledTime - m_LostSince > 0.5f;
        }

        void DropGrab()
        {
            m_Gesture = Gesture.None;
            m_LostSince = -1f;
            var head = m_Core.Head.position;
            var toWindow = m_Window.position - head;
            if (!PlaceOnWall(new Ray(head, toWindow.normalized)))
                Debug.Log("[Apps] Dropped the window");
            Reanchor();
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
