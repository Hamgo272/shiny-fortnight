using CardboardXR.Demos.Apps;
using NUnit.Framework;
using UnityEngine;

namespace CardboardXR.Stereo.Tests
{
    public class TwoFingerScrollTests
    {
        // A right hand held up, in meters: wrist at the origin, knuckles 8-9 cm above it.
        static Vector3[] Hand(bool indexOut, bool middleOut, bool ringOut, bool littleOut, bool pinching)
        {
            var j = new Vector3[21];
            j[0] = Vector3.zero;
            j[5] = new Vector3(-0.03f, 0.085f, 0f);
            j[9] = new Vector3(-0.01f, 0.09f, 0f);
            j[13] = new Vector3(0.01f, 0.085f, 0f);
            j[17] = new Vector3(0.03f, 0.075f, 0f);
            Vector3 Tip(Vector3 knuckle, bool straight) =>
                straight ? knuckle + new Vector3(0f, 0.08f, 0f) : knuckle + new Vector3(0f, -0.03f, 0.03f);
            j[8] = Tip(j[5], indexOut);
            j[12] = Tip(j[9], middleOut);
            j[16] = Tip(j[13], ringOut);
            j[20] = Tip(j[17], littleOut);
            j[4] = pinching ? j[8] + new Vector3(0.005f, 0f, 0f) : new Vector3(-0.08f, 0.04f, 0f);
            for (var i = 0; i < j.Length; i++)
            {
                if (j[i] == Vector3.zero && i != 0)
                    j[i] = j[0];
            }
            return j;
        }

        const float k_Away = -0.6f;

        [Test]
        public void TwoFingersOutWithThePalmAwayIsThePose() =>
            Assert.That(TwoFingerScroll.IsPose(Hand(true, true, false, false, false), k_Away), Is.True);

        [Test]
        public void ThePalmTowardYouIsNot() =>
            Assert.That(TwoFingerScroll.IsPose(Hand(true, true, false, false, false), 0.6f), Is.False);

        [Test]
        public void AnOpenHandIsNot() =>
            Assert.That(TwoFingerScroll.IsPose(Hand(true, true, true, true, false), k_Away), Is.False);

        [Test]
        public void OneFingerIsNot() =>
            Assert.That(TwoFingerScroll.IsPose(Hand(true, false, false, false, false), k_Away), Is.False);

        [Test]
        public void APinchIsNot() =>
            Assert.That(TwoFingerScroll.IsPose(Hand(true, true, false, false, true), k_Away), Is.False);

        [Test]
        public void ScrollingWaitsForThePoseToHold()
        {
            var scroll = new TwoFingerScroll();
            Assert.That(scroll.Update(true, 1.0f, 0.05f), Is.EqualTo(0));
            Assert.That(scroll.IsScrolling, Is.False);
            scroll.Update(true, 1.0f, 0.2f);
            Assert.That(scroll.IsScrolling, Is.True);
        }

        [Test]
        public void HandUpScrollsDownAndHandDownScrollsUp()
        {
            var scroll = new TwoFingerScroll();
            scroll.Update(true, 1.0f, 0.2f);
            // 5 cm up: 2.5 notches, sent as 2 with half carried.
            Assert.That(scroll.Update(true, 1.05f, 0.016f), Is.EqualTo(-2));
            Assert.That(scroll.Update(true, 1.06f, 0.016f), Is.EqualTo(-1));
            Assert.That(scroll.Update(true, 1.0f, 0.016f), Is.EqualTo(3));
        }

        [Test]
        public void HoldingThePoseShowsItIsAboutToScroll()
        {
            var scroll = new TwoFingerScroll();
            Assert.That(scroll.IsArming, Is.False);
            scroll.Update(true, 1.0f, 0.05f);
            Assert.That(scroll.IsArming, Is.True);
            scroll.Update(true, 1.0f, 0.2f);
            Assert.That(scroll.IsArming, Is.False);
            Assert.That(scroll.IsScrolling, Is.True);
        }

        [Test]
        public void TheClickTickIsShortAndInRange()
        {
            var samples = CardboardXR.Interaction.ClickSound.Samples(44100);
            Assert.That(samples.Length, Is.InRange(1000, 1200));
            foreach (var sample in samples)
                Assert.That(sample, Is.InRange(-1f, 1f));
            // It dies away: the last millisecond is near silent.
            Assert.That(Mathf.Abs(samples[^1]), Is.LessThan(0.01f));
        }

        [Test]
        public void AFrameDroppedDoesNotEndIt()
        {
            var scroll = new TwoFingerScroll();
            scroll.Update(true, 1.0f, 0.2f);
            scroll.Update(false, 0f, 0.05f);
            Assert.That(scroll.IsScrolling, Is.True);
            scroll.Update(false, 0f, 0.3f);
            Assert.That(scroll.IsScrolling, Is.False);
        }
    }
}
