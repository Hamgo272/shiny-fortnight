using Unity.Collections;
using UnityEngine;

namespace CardboardXR.Demos.Apps
{
    public enum AppWindowState
    {
        Failed = -3,
        PermissionDenied = -2,
        NoShizuku = -1,
        Idle = 0,
        WaitingForPermission = 1,
        Starting = 2,
        Running = 3,
    }

    // The C# side of the Android plugin AppWindows.java. It does nothing off Android, where there are
    // no other apps to show. The two frame buffers are allocated here and written from Java, so a
    // frame reaches Unity without a copy through JNI.
    public sealed class AppWindowBridge
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaClass m_Plugin;
#endif
        NativeArray<byte> m_First;
        NativeArray<byte> m_Second;

        public AppWindowState State
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return m_Plugin == null ? AppWindowState.Idle : (AppWindowState)m_Plugin.CallStatic<int>("state");
#else
                return AppWindowState.NoShizuku;
#endif
            }
        }

        // Goes up with each finished frame.
        public long Sequence
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return m_Plugin == null ? 0 : m_Plugin.CallStatic<long>("sequence");
#else
                return 0;
#endif
            }
        }

        // The finished frame for a sequence number: valid until the next-but-one frame starts.
        public NativeArray<byte> Frame(long sequence) => sequence % 2 == 0 ? m_First : m_Second;

        public void Start(int width, int height, int dpi, string launchPackage)
        {
            var size = width * height * 4;
            m_First = new NativeArray<byte>(size, Allocator.Persistent);
            m_Second = new NativeArray<byte>(size, Allocator.Persistent);
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin = new AndroidJavaClass("com.brenpoly.cardboardxr.windows.AppWindows");
            var first = AndroidJNI.NewDirectByteBuffer(m_First);
            var second = AndroidJNI.NewDirectByteBuffer(m_Second);
            var method = AndroidJNI.GetStaticMethodID(m_Plugin.GetRawClass(), "start",
                "(Ljava/nio/ByteBuffer;Ljava/nio/ByteBuffer;IIILjava/lang/String;Ljava/lang/String;)V");
            var appPackage = AndroidJNI.NewStringUTF(Application.identifier);
            var launch = AndroidJNI.NewStringUTF(launchPackage);
            AndroidJNI.CallStaticVoidMethod(m_Plugin.GetRawClass(), method, new[]
            {
                new jvalue { l = first }, new jvalue { l = second }, new jvalue { i = width }, new jvalue { i = height },
                new jvalue { i = dpi }, new jvalue { l = appPackage }, new jvalue { l = launch },
            });
            AndroidJNI.DeleteLocalRef(first);
            AndroidJNI.DeleteLocalRef(second);
            AndroidJNI.DeleteLocalRef(appPackage);
            AndroidJNI.DeleteLocalRef(launch);
#endif
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin?.CallStatic("stop");
            m_Plugin?.Dispose();
            m_Plugin = null;
#endif
            if (m_First.IsCreated)
                m_First.Dispose();
            if (m_Second.IsCreated)
                m_Second.Dispose();
        }

        public void Launch(string packageName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin?.CallStatic("launch", packageName);
#endif
        }

        public void Tap(int x, int y)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin?.CallStatic("tap", x, y);
#endif
        }

        public void Swipe(int fromX, int fromY, int toX, int toY, int milliseconds)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin?.CallStatic("swipe", fromX, fromY, toX, toY, milliseconds);
#endif
        }

        // An Android key code, such as KEYCODE_BACK.
        public void Key(int keyCode)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            m_Plugin?.CallStatic("key", keyCode);
#endif
        }
    }
}
