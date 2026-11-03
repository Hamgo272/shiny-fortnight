# app-windows: progress and handover

Notes for picking this branch up on another machine. `main` mirrors `brenpoly/CardboardXR`; this
branch (`app-windows`) holds our additions on top of it. Test phone: Galaxy S23 FE (SM-S711B,
One UI 6.1) with Shizuku.

## Done

### Apps demo (YouTube on a floating window)

- **Pinch fix** (`1725888`). Turning on Passthrough from the settings panel in a demo that asked
  for 3DoF with hands off (Apps, 360 Video) switched to 6DoF. That stopped the 3DoF pinch sensor
  while hand tracking stayed off, so nothing saw pinches and the settings X couldn't be pressed.
  Confirmed on the phone's log (`[Pinch] Off` right after `[Passthrough] On`, then no input at all).
  `PinchInput` now turns hand tracking on whenever it's 6DoF with pinching on. Apps now asks for
  6DoF + passthrough + hands, so YouTube opens in passthrough.
- **Window controls** (`AppWindowDemo.cs`, the demo is now an `IPinchHandler`):
  - pinch and release on the window: tap
  - pinch and move the hand: drag, sent as swipes (one when the fingers open, more on a long drag),
    so a feed scrolls with the hand
  - pinch the window's edge (it lights up under the gaze) and pull: resize, keeping 16:9
  - Move button, top-left corner: the window hides; the next pinch puts it where you're looking
  - Close button, top-right corner: closes the app and leaves
  - in 3DoF (no hand position) a pinch can only tap, as before
- **Arrange windows** (settings toolbar, `view_agenda` icon). `FloatingWindows` is a registry of
  the scene's windows and panels; Arrange puts them on an arc around you at eye level, widest in
  the middle, then right and left, with panels in a row below, like Android XR's reposition.
  The Apps window (with its corner buttons), its panel and the Portal panel register themselves.
  Other demos' panels can join with `FloatingWindows.Register(...)`.

### Portal: the Moon (second world, now the default)

- `PortalWorld.Kind` picks the world (Moon or Night). The Portal panel has Moon / Night, Suit glove /
  Real hand, Move portal and Smooth soil.
- `MoonWorld`: three nested height grids from `MoonTerrain`: a 940 m square at 5 m, 80 m at 25 cm,
  and a 3 × 3 m soil patch by the door at 1 cm. Each coarser grid dips under the finer one. Craters
  of every size (bowl, rim, r^-3 ejecta, rays on fresh ones), the door area kept level. The heights
  are worked out on a thread while the portal rises.
- `Moon.shader` (CardboardXR/Moon): main light only, with its shadows. Lommel-Seeliger lunar
  scattering with an opposition surge, scanned regolith detail plus a 31 m macro layer, vertex
  albedo, and a faint earthshine. The room light (Core's, which follows the passthrough estimate)
  is ignored.
- Sun 11° up as `RenderSettings.sun`. While the Moon exists, shadow distance is 16 m at 2048. The
  shadow catcher on real floor planes is off, so the Moon's ground doesn't darken your floor. All of
  it is put back afterwards.
- Black sky (two half domes), 5000 star quads, and the Earth (NASA Blue Marble) 1.9° wide, lit
  for a phase.
- Scanned moon rocks (Poly Haven, CC0).
- `MoonSoil`: fingertips press dents with a raised rim, and the pressed soil darkens; boot prints
  are stamped as you walk across the patch. `MoonDust`: grains fly ballistic arcs at 1.62 m/s².
- `MoonHand`: from `HandTracker.Joints`, either the EVA glove (fabric, rubber tips, metal ring and
  band, ribbed sleeve, casting shadows) or the real hand (the same shapes in DepthMask, so the
  passthrough image shows your hand, with a shadow-only glove for the shadow).
- Assets: `Assets/_Project/Demos/Portal/Moon/`. `MoonAssetsBuilder` (editor) sets the imports and
  makes the materials and `Moon Assets.asset` on the first build, and gives them to `Portal.prefab`.
  Credits are in `THIRD_PARTY_NOTICES.md`.
- An interactive browser preview of the look: https://claude.ai/artifact/K5c11E7tf2HrtEFfGsJfMx

### Tests

`Tests/EditMode/MoonTests.cs`: level ground at the door, deterministic terrain, fingertip
dent and rim, no change above the soil or at its edge, boot print depth, and window layout order
and panels below.

## Building on Windows

The `Tools/*.sh` scripts are for the Mac. On Windows, with Unity 6000.6.0f1 (+ Android modules,
OpenJDK) from Unity Hub:

```sh
U="/c/Program Files/Unity/Hub/Editor/6000.6.0f1/Editor/Unity.exe"
"$U" -batchmode -quit -projectPath "$(pwd)" -buildTarget Android \
  -executeMethod ProjectBootstrap.StereoBuilder.BuildAndroid -logFile Logs/build.log
adb install -r Builds/CardboardXR.apk
```

An APK built on one computer is signed with that computer's debug key, so it won't install over
one built on another: uninstall first (`adb uninstall com.brenpoly.cardboardxr`). That removes the
app's data on the phone (pushed videos, the saved viewer).

Wireless debugging over Tailscale works: `adb pair <ip>:<pair port> <code>`, then
`adb connect <ip>:<port>` (the port on the Wireless debugging screen). Turn on Stay awake, or the
connection drops when the screen sleeps.

A second copy installs beside the Mac-built app, so that one stays as a backup:

```sh
CARDBOARDXR_BUNDLE_ID=com.brenpoly.cardboardxr.win CARDBOARDXR_APP_NAME="CardboardXR Win" "$U" -batchmode -quit ...
```

The build puts the app id and name back afterwards, so they aren't left in ProjectSettings. A Unity
build restarts adb, which drops a wireless connection: `adb connect` again before installing.

## For the MacBook

- `IosProMotion.cs` is now inside `#if UNITY_IOS`. `UnityEditor.iOS` only exists where iOS build
  support is installed, and the Windows machine has only Android. On the Mac, with the iOS target
  active, it compiles and runs exactly as before. Nothing to do.
- The first build there makes `Moon Assets.asset` if it's missing (it's checked in now), and the
  Core scene's toolbar gets the Arrange button: Core is rebuilt on every build.
- The URP asset, `Generated/` materials, fonts and ProjectSettings change on every build as Unity
  regenerates them. They're left out of these commits on purpose.

## Status and open items

Tested on the S23 FE (`CardboardXR Win` build, 2026-11-04): the settings X closes with passthrough
on; Apps opens in passthrough; tap, drag, edge resize, Move, Arrange and the passthrough soft fill
all work. Not yet tried on the phone: the Moon.

To do next:

- Drag and resize are too sensitive (hand motion amplified up to ~7x): cap it near 3x.
- Apps runs hot (`thermal status 4`, display down to 60 Hz): AR, hand tracking, YouTube's decoder
  and a CPU frame copy all at once. Ideas: hand tracking at a lower rate in Apps, 60 fps target,
  skip copying unchanged frames.

Known limits:

- The real-hand cutout comes from the joints, so a thin edge of the room shows around the fingers,
  and it trails a fast hand slightly.
- The Moon is seen flat (passthrough makes both eyes the same picture). Switching to true stereo
  once you're through the door is an idea to try.
- Android XR SDK: it targets Android XR headsets and glasses; Unity's Android XR support needs
  such a device, so it won't run on the S23 FE. The window behaviours above follow Android XR's
  instead.
