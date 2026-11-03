using CardboardXR.Demos.Portal;
using CardboardXR.UI;
using NUnit.Framework;
using UnityEngine;

namespace CardboardXR.Stereo.Tests
{
    public class MoonTests
    {
        [Test]
        public void GroundIsLevelAtTheDoor()
        {
            Assert.That(MoonTerrain.Height(0f, 0f, 0f), Is.EqualTo(0f).Within(1e-4f));
            // Within a step or two of the door the ground stays within a few centimeters of the floor.
            for (var z = -1f; z <= 1.5f; z += 0.25f)
            {
                for (var x = -0.5f; x <= 0.5f; x += 0.25f)
                    Assert.That(Mathf.Abs(MoonTerrain.Height(x, z, 0.3f)), Is.LessThan(0.08f), $"at {x}, {z}");
            }
        }

        [Test]
        public void GroundIsTheSameEveryTime()
        {
            Assert.That(MoonTerrain.Height(12.3f, 20.1f, 0f), Is.EqualTo(MoonTerrain.Height(12.3f, 20.1f, 0f)));
        }

        static MoonSoil FlatSoil() => new(new HeightGrid(-0.5f, -0.5f, 1f, 100, (x, z) => (0f, 1f)));

        [Test]
        public void FingertipDentsTheSoilAndRaisesARim()
        {
            var soil = FlatSoil();
            var moved = soil.Press(new Vector3(0f, 0.005f, 0f), 0.01f);
            Assert.That(moved, Is.GreaterThan(0f));
            Assert.That(soil.Grid.HeightAt(0f, 0f), Is.EqualTo(-0.005f).Within(0.001f));
            Assert.That(soil.Grid.HeightAt(0.016f, 0f), Is.GreaterThan(0f));
            Assert.That(soil.Grid.HeightAt(0.016f, 0f), Is.LessThanOrEqualTo(0.007f + 1e-5f));
        }

        [Test]
        public void FingertipAboveTheSoilLeavesIt()
        {
            var soil = FlatSoil();
            Assert.That(soil.Press(new Vector3(0f, 0.02f, 0f), 0.01f), Is.EqualTo(0f));
            Assert.That(soil.Grid.HeightAt(0f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void SoilAtTheEdgeDoesNotMove()
        {
            var soil = FlatSoil();
            soil.Press(new Vector3(0.495f, 0f, 0f), 0.01f);
            Assert.That(soil.Grid.HeightAt(0.5f, 0f), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void BootPrintSinksAboutACentimeter()
        {
            var soil = FlatSoil();
            soil.Boot(0f, 0f, 0f);
            var h = soil.Grid.HeightAt(0f, -0.08f);
            Assert.That(h, Is.LessThan(-0.006f));
            Assert.That(h, Is.GreaterThan(-0.02f));
        }

        static FloatingWindows.Window Window(float width, bool panel = false) =>
            new() { Name = "W", Width = () => width, Place = _ => { }, IsPanel = panel };

        [Test]
        public void WindowsArrangeWidestInTheMiddleThenRightAndLeft()
        {
            var windows = new[] { Window(0.5f), Window(2.4f), Window(0.8f) };
            var poses = FloatingWindows.Layout(windows, Vector3.zero, 0f);
            // The widest is straight ahead; the next widest to its right, then the narrowest to the left.
            Assert.That(poses[1].position.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(poses[2].position.x, Is.GreaterThan(1f));
            Assert.That(poses[0].position.x, Is.LessThan(-1f));
            foreach (var pose in poses)
            {
                Assert.That(pose.position.magnitude, Is.EqualTo(FloatingWindows.k_Distance).Within(1e-3f));
                Assert.That(pose.position.y, Is.EqualTo(0f).Within(1e-4f));
            }
        }

        [Test]
        public void PanelsGoBelowTheWindows()
        {
            var windows = new[] { Window(2f), Window(0.6f, true) };
            var poses = FloatingWindows.Layout(windows, Vector3.zero, 90f);
            Assert.That(poses[1].position.y, Is.LessThan(-0.3f));
            // Turned with the user: ahead is +x at a yaw of 90 degrees.
            Assert.That(poses[0].position.x, Is.EqualTo(FloatingWindows.k_Distance).Within(1e-3f));
        }
    }
}
