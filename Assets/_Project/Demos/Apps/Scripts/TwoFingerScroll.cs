using UnityEngine;

namespace CardboardXR.Demos.Apps
{
    // Scrolling a window with two fingers, like a laptop's touchpad: the index and middle fingers held
    // out together, the ring and little fingers curled, the thumb clear of the index finger, and the
    // back of the hand toward you (palm away), then the hand moved up or down. Moving it up moves the
    // page up, as a finger drag would.
    //   That pose can't be a pinch (the thumb is away from the index finger, so a tap never turns into
    //   a scroll) or bring up the settings gear (which needs the palm toward you). The pose has to hold
    //   k_HoldSeconds before scrolling starts, so a hand passing through it on the way to a pinch
    //   doesn't scroll. Pure logic from joint positions and the palm's facing, so it's tested off the
    //   phone; AppWindowDemo feeds it each frame and sends the notches as mouse-wheel scrolls.
    public class TwoFingerScroll
    {
        // Finger tip to the palm's middle, in palm lengths (wrist to middle knuckle): straight beyond
        // k_Straight, curled within k_Curled.
        public const float k_Straight = 1.05f;
        public const float k_Curled = 0.85f;
        // The thumb tip at least this many palm lengths from the index finger's tip: not a pinch.
        public const float k_ThumbClear = 0.55f;
        // The back of the hand toward the camera: PalmGesture.Facing below this.
        public const float k_PalmAway = -0.25f;
        public const float k_HoldSeconds = 0.15f;
        // Let go of the pose this long and scrolling stops (tracking drops a frame now and then).
        public const float k_ReleaseSeconds = 0.2f;
        // One wheel notch per this much hand movement, in meters.
        public const float k_MetersPerNotch = 0.02f;

        const int k_Wrist = 0;
        const int k_ThumbTip = 4;
        const int k_IndexKnuckle = 5;
        const int k_IndexTip = 8;
        const int k_MiddleKnuckle = 9;
        const int k_MiddleTip = 12;
        const int k_RingKnuckle = 13;
        const int k_RingTip = 16;
        const int k_LittleKnuckle = 17;
        const int k_LittleTip = 20;

        float m_PoseFor;
        float m_LostFor;
        float m_LastHeight;
        float m_Carried;

        public bool IsScrolling { get; private set; }

        // The pose is held but hasn't held long enough to scroll yet: shown as a faint frame.
        public bool IsArming => !IsScrolling && m_PoseFor > 0f;

        // Whether 21 joints (MediaPipe's order, in meters) are in the scroll pose, given how the palm
        // faces the camera (PalmGesture.Facing: negative for its back).
        public static bool IsPose(Vector3[] joints, float palmFacing)
        {
            if (joints == null || joints.Length < 21 || palmFacing > k_PalmAway)
                return false;
            var palm = (joints[k_Wrist] + joints[k_IndexKnuckle] + joints[k_MiddleKnuckle] + joints[k_RingKnuckle] +
                        joints[k_LittleKnuckle]) / 5f;
            var length = Vector3.Distance(joints[k_Wrist], joints[k_MiddleKnuckle]);
            if (length < 1e-3f)
                return false;
            float Reach(int tip) => Vector3.Distance(joints[tip], palm) / length;
            return Reach(k_IndexTip) > k_Straight && Reach(k_MiddleTip) > k_Straight &&
                   Reach(k_RingTip) < k_Curled && Reach(k_LittleTip) < k_Curled &&
                   Vector3.Distance(joints[k_ThumbTip], joints[k_IndexTip]) / length > k_ThumbClear;
        }

        // A frame: whether the pose is seen (IsPose, with the gaze on the window), the hand's height in
        // meters, and the time since the last frame. Returns the wheel notches to send now: positive
        // scrolls up (the hand moved down), as Android's AXIS_VSCROLL.
        public int Update(bool pose, float height, float deltaTime)
        {
            if (!pose)
            {
                m_PoseFor = 0f;
                if (IsScrolling)
                {
                    m_LostFor += deltaTime;
                    if (m_LostFor >= k_ReleaseSeconds)
                    {
                        IsScrolling = false;
                        Debug.Log("[Apps] Two-finger scroll ended");
                    }
                }
                return 0;
            }
            m_LostFor = 0f;
            if (!IsScrolling)
            {
                m_PoseFor += deltaTime;
                if (m_PoseFor < k_HoldSeconds)
                    return 0;
                IsScrolling = true;
                m_LastHeight = height;
                m_Carried = 0f;
                Debug.Log("[Apps] Two-finger scroll started");
                return 0;
            }
            // Hand up, page up: the content follows the fingers, so the view scrolls down.
            m_Carried += (m_LastHeight - height) / k_MetersPerNotch;
            m_LastHeight = height;
            // A hair of tolerance: 2.5 then 0.5 more must make 3, not 2.9999 truncated to 2.
            var notches = (int)(m_Carried + (m_Carried > 0f ? 1e-4f : -1e-4f));
            m_Carried -= notches;
            return notches;
        }

        public void Reset()
        {
            m_PoseFor = 0f;
            m_LostFor = 0f;
            m_Carried = 0f;
            IsScrolling = false;
        }
    }
}
