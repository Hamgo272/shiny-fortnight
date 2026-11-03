using UnityEngine;

namespace CardboardXR.UI
{
    // The settings panel's Arrange button: gathers the scene's floating windows around the user
    // (FloatingWindows.Arrange). On the head, which it arranges them around.
    public class WindowArranger : MonoBehaviour
    {
        [SerializeField] Transform m_Head;

        public void Arrange() => FloatingWindows.Arrange(m_Head != null ? m_Head : transform);
    }
}
