using CardboardXR.Demos;
using CardboardXR.Demos.Apps;
using NUnit.Framework;

namespace CardboardXR.Stereo.Tests
{
    public class BrowserTests
    {
        [TestCase("wikipedia.org", "https://wikipedia.org")]
        [TestCase("m.youtube.com/feed", "https://m.youtube.com/feed")]
        [TestCase("  example.com  ", "https://example.com")]
        [TestCase("http://192.168.1.10:8080", "http://192.168.1.10:8080")]
        [TestCase("https://claude.ai", "https://claude.ai")]
        public void AddressesOpenAsAddresses(string typed, string expected) =>
            Assert.That(WindowKeyboard.ToUrl(typed), Is.EqualTo(expected));

        [TestCase("weather today", "https://www.google.com/search?q=weather%20today")]
        [TestCase("unity", "https://www.google.com/search?q=unity")]
        [TestCase("v1.0 release notes", "https://www.google.com/search?q=v1.0%20release%20notes")]
        [TestCase(".com", "https://www.google.com/search?q=.com")]
        public void AnythingElseIsASearch(string typed, string expected) =>
            Assert.That(WindowKeyboard.ToUrl(typed), Is.EqualTo(expected));

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void NothingTypedOpensNothing(string typed) => Assert.That(WindowKeyboard.ToUrl(typed), Is.Null);

        [Test]
        public void VolumeIsClampedOffTheDevice()
        {
            DeviceVolume.Set(1.7f);
            Assert.That(DeviceVolume.Get(), Is.EqualTo(1f));
            DeviceVolume.Set(-0.3f);
            Assert.That(DeviceVolume.Get(), Is.EqualTo(0f));
        }
    }
}
