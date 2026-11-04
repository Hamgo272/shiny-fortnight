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
- **Window controls** (`AppWindowDemo.cs`), tested on the S23 FE:
  - a pinch on the window is a tap, through the ordinary gaze click (`WindowClick`), exactly as the
    first version. An in-between version took pinches itself to drag and to resize from every
    edge; taps kept turning into drags or resizes (the window lit blue and moved), so that's gone.
    Scroll with the panel's Up/Down. Drag-to-scroll could come back later, in a way that doesn't
    take taps.
  - the demo takes only two pinches (`PinchRouter.Handler`): on an L handle just outside a bottom
    corner, which resizes from that corner (16:9 kept), and, while moving, the one that puts the
    window down
  - Move and Close sit in a small bar centred above the window (at the top corners they were
    pressed by taps meant for YouTube's own menu and back buttons). Move works as in Android XR:
    the window follows the gaze until a pinch puts it down. Close closes the app and leaves.
  - the window otherwise stays where it is: only Move, Arrange or a handle shift it
  - no cubes in Apps: a pinch that missed the window and hit the floor placed one, as in the lobby
  - the settings gear is unchanged: it appears only with the palm toward you
  - 60 fps while the window is up, for heat. A lower render scale was tried and removed: changing
    it mid-session through `StereoRig.Rebuild` left the settings panel unclickable after leaving.
- **The library tile** is now "YouTube" with a play-button icon (`smart_display`).
- **App id** is `com.aira.cardboardxr` everywhere (Android, iOS, the Mac scripts, README).
- **Arrange windows** (settings toolbar, `view_agenda` icon). `FloatingWindows` is a registry of
  the scene's windows and panels; Arrange puts them on an arc around you at eye level, widest in
  the middle, then right and left, with panels in a row below, like Android XR's reposition.
  The Apps window (with its button bar), its panel and the Portal panel register themselves.
  Other demos' panels can join with `FloatingWindows.Register(...)`.

### Browser window, and Controls (volume, brightness)

- **Browser** (library tile, globe icon, catalog id `browser`). The same scene and demo as YouTube
  (`AppWindowDemo` checks `DemoManager.Current.Id`); it opens Chrome, or Samsung Internet if there's
  no Chrome (`WindowService.launch` takes a comma-separated list and opens the first installed).
  Home opens google.com. The panel gets a **Type** button for `WindowKeyboard`: an in-world keyboard
  that collects what's typed, then **Go** opens it as an address (or a Google search, see
  `WindowKeyboard.ToUrl`) through a VIEW intent on the window's display, and **Type** sends it to the
  field focused on the page (`input text`, then Enter). Android's own keyboard is turned off for the
  window's display (see below), since it would come up on the phone's screen, over the stereo view.
- **Controls** (settings toolbar, next to Apps, `tune` icon): a card under the panel with media volume
  (Android's music stream, so it sets YouTube's and the browser's too) and screen brightness
  (`ScreenBrightness.Level`, now kept between runs; the `brightness` launch option still wins).
  Pinch along a slider to set it there, or use - and +.
- **Up/Down freeze fix.** On the phone, Up/Down in the browser froze the passthrough, went black for
  about a second, then came back scrolled. They were 250 ms swipes through `input`, and touching the
  window's display took focus from Unity, which stops drawing without it. Now they're mouse-wheel
  events injected straight from the shell service (`InputManagerGlobal.injectInputEvent`, an
  `ACTION_SCROLL` with `AXIS_VSCROLL` on the window's display), which don't touch the display. The
  S23 FE's `input` command has no `scroll` (checked). The swipe is kept as a fallback if injecting
  fails. The display is also made with Android 14's `STEAL_TOP_FOCUS_DISABLED` (old flags if
  refused), so taps shouldn't take focus either. In the log, look for "Display made with
  STEAL_TOP_FOCUS_DISABLED" and "Couldn't inject a scroll".
- **Taps recentred the room (fixed, found in the phone's log).** Every tap on a window was followed
  0.6-1 s later by "ARCore tracking lost", the session restarting at (0,0,0), then "position jumped
  10-20 cm; re-anchoring": the passthrough glitched and the view shifted. The cause was
  `returnFocus()` after every input: `am start` of Unity's activity delivers a new intent, which
  pauses and resumes it (`APP_CMD_PAUSE`, ARCore `Session::Pause`). With `STEAL_TOP_FOCUS_DISABLED`
  focus never leaves Unity (the log showed only display 55's own focus changing), so it no longer
  runs after taps, scrolls, launches or links. If Unity does lose focus (`OnApplicationFocus(false)`)
  it asks for it back after 300 ms unless it paused meanwhile (the user leaving), with
  `ActivityTaskManager.setFocusedTask`, which pauses nothing. Taps are injected directly now (press
  and release), not through the `input` process.
- Two-finger scroll notches are summed and sent at most every 0.12 s: one per frame made the browser
  (which animates each notch) stutter. YouTube was fine either way.
- **Moon cinema** (window panel's Moon / Room button, YouTube and Browser): the Moon instead of
  the room, built by `MoonWorld` with `Lite` set (sky, Earth, ground and rocks; no sun shadows, a
  coarser near ground, no soil, dust, boot prints or hand). Passthrough goes off; tracking stays 6DoF
  with hands (the user asked for 6DoF after trying 3DoF, which ran cooler but couldn't lean, scroll
  with two fingers or grab). Room switches passthrough back on. The Moon's ground goes on the real
  floor (`RoomBoundary.FloorHeight`) or 1.5 m below the eyes, facing the gaze, and the window is
  re-centred, a few frames after the switch. The Apps scene gets the Moon's assets on
  each build (`DemoScenes.EnsureAppsMoon`). Opening YouTube from Portal still leaves the Moon (demos
  don't overlap): use the Moon button instead.
- **Fist grab: removed after the phone test.** A pinch curls the fingers enough to pass the fist test, so every tap on the window grabbed it (blue outline, window following the hand). The user wants the window moved only with the Move button. `FollowFist` is kept unused. What it was: look at the window and make a fist, and the window
  follows the hand round you at its distance; open the hand to let go. 6DoF with hands only. The
  parked version held every tap back 150 ms in case a fist was forming; now taps are instead ignored
  while grabbed and for 0.5 s after (the pinch a closing fist passes through releases when the hand
  opens). The same guard ignores taps while the two-finger pose is held: the user saw scrolling also
  click the page.
- **Two-finger scroll** (the user's idea): index and middle fingers out, ring and little curled, thumb
  clear, the back of the hand toward you, with the gaze on the window; move the hand up or down and
  the window scrolls where you look (`TwoFingerScroll`, 2 cm a wheel notch, the page following the
  fingers). Holds 0.15 s before starting. It can't be a pinch (thumb away) or the settings gear
  (palm away). The finger thresholds are guesses from hand proportions: tune them on the phone
  (the log says "Two-finger scroll started").
- **Immediate feedback** (a gesture shows on the frame it registers, before the app answers):
  - the reticle draws in and turns solid while a pinch is held, and pings a ring out on release
    (`GazeReticle.Press`, everywhere);
  - a soft 25 ms tick on every gaze press, made in code (`ClickSound`; launch option `clicks 0` mutes it);
  - a button's pressed wash shows at least 150 ms (`M3StateLayer.k_MinPressSeconds`), so a quick
    pinch is still seen on it;
  - a ring spreads from the tapped point on an app window (the app can take a few hundred ms);
  - the window's frame lights while it's scrolled (two fingers), moved or resized, and faintly while
    the two-finger pose is held, about to scroll.
- The on-screen keyboard is turned off for the window's display through
  `IWindowManager.setDisplayImePolicy(id, HIDE)` by reflection, since this phone's `wm` command has no
  `set-display-ime-policy` (checked).
- Found on the phone: the window is a whole Android display. An app on it that opens another (an ad
  opening Gemini, Gemini opening the browser) opens it there too, replacing the one before. So any
  app can run in a window, which is what a launcher would build on.
- Parked, untested: branch `wip-walls-fist-grab` (window on walls, fist grab, ARCore anchoring, an
  Open apps list). It also delayed every tap by 150 ms, which changes the tap feel; the Apps library
  already shows what's running, so the Open list isn't needed.

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
one built on another: uninstall first (`adb uninstall com.aira.cardboardxr`). That removes the
app's data on the phone (pushed videos, the saved viewer).

Wireless debugging over Tailscale works: `adb pair <ip>:<pair port> <code>`, then
`adb connect <ip>:<port>` (the port on the Wireless debugging screen). Turn on Stay awake, or the
connection drops when the screen sleeps.

A second copy can install beside another build (another app id), so that one stays as a backup:

```sh
CARDBOARDXR_BUNDLE_ID=com.aira.cardboardxr.test CARDBOARDXR_APP_NAME="CardboardXR Test" "$U" -batchmode -quit ...
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

Tested on the S23 FE (`com.aira.cardboardxr`, 2026-11-04): the settings X closes with passthrough
on and after leaving YouTube; YouTube opens in passthrough; taps on the window work; the window
stays put; Move, Close, the corner handles, Arrange and the matte passthrough fill work. Not yet
tried on the phone: the Moon.

- Passthrough fill: the screen is drawn 2.6x the camera image; outside it the image's edge is a
  matte blur (eight reads of a soft mip, desaturated, dimmed and dithered). A first version read
  one small mip and looked banded and watery. `passFill 0` turns it off.
- Apps still runs warm (60 fps helps). See "Cooler running" below.

Known limits:

- The real-hand cutout comes from the joints, so a thin edge of the room shows around the fingers,
  and it trails a fast hand slightly.
- The Moon is seen flat (passthrough makes both eyes the same picture). Switching to true stereo
  once you're through the door is an idea to try.
- Android XR SDK: it targets Android XR headsets and glasses; Unity's Android XR support needs
  such a device, so it won't run on the S23 FE. The window behaviours above follow Android XR's
  instead.

## Future ideas

### Next, in order (asked for 2026-11-04)

1. App launcher on the window: list the phone's apps from the shell service
   (`cmd package query-activities --brief -a android.intent.action.MAIN -c android.intent.category.LAUNCHER`)
   with their icons, and open any of them on a window; a recent-apps switcher to go back to one.
2. Spotify, shown in a custom 3D style (reference image from the user) rather than only its phone UI:
   check what Spotify's app and Web API allow (now playing, artwork, controls).
3. A custom look for the icons and UI, distinct from brenpoly's: on hold. The first proposal
   (dark glass, cyan, Sora/Inter, line icons) wasn't liked; keep the existing Material UI and icons.

### Live portal into someone else's room (Insta360)

Another person with the app and an Insta360 camera opens their room to you: the Portal's doorway
shows their room as their 360 camera films it, and stepping through puts you inside their 360°
view, with your own room behind you through the door.

What already exists:

- The 360 sphere: the 360 Video demo maps equirectangular video onto a sphere (`VideoSphere`).
- The door: the Portal shows a world only through the doorway and swaps sides as you walk through.
  A third world next to Night and Moon, "Live", would be a video sphere instead of terrain
  (`PortalWorld.Kind`).
- Frames from an Android-side source into a texture: the YouTube window does this already
  (`AppWindowBridge`).

What you'd get, and the limits:

- Outside, their room is seen through the door; inside, it surrounds you.
- A 360 camera films from one point: you can look around but not walk around or lean to see behind
  things, so once through, the sphere follows your head's position (3DoF in there). Mono 360 looks
  flat, as the Moon does in passthrough.
- A 360 stream spreads its pixels all round, so even "5.7K" looks soft in a viewer: a sense of
  place, not text you can read.

The hard part is the live link, not the visuals:

- Insta360 X3/X4 can live stream over RTMP (to YouTube or your own server). RTMP or HLS adds about
  2–10 s of delay: fine for looking around, awkward for talking.
- WebRTC gets it to about 0.3–1 s but needs a small signalling server, and a relay when not on the
  same network. Tailscale (already used for wireless adb) makes connecting two people much easier.
- Unity's video player on Android handles live RTSP/RTMP poorly, so it likely needs an Android-side
  player (ExoPlayer, or a WebRTC library) feeding frames into the sphere, as the YouTube window does.
- Two-way audio, and something for them to see of you: your passthrough camera in a window, or an
  avatar.

A first version:

1. The Insta360 streams RTMP to a small server (for example MediaMTX on a PC) over Tailscale.
2. The app adds a "Live" portal world that plays that stream on a sphere behind the door.

### Cooler running (from the S23 FE's thermal status 4 in Apps)

- Run the hand model on the GPU (Unity Inference's GPU backend), or an NPU through another
  runtime: likely the biggest win, to be measured behind a launch option.
- Pass the YouTube window's frames to the GPU directly (a shared texture) instead of copying them
  through the CPU.
- Lower the forced screen brightness in heavy demos (the `brightness` launch option exists).
- A lower render scale in heavy demos. Tried and removed: changing it mid-session through
  `StereoRig.Rebuild` (which remakes the eye cameras) left the settings panel unclickable after
  leaving YouTube, with every pinch falling through to the real surface behind it. A retry should
  resize the eye textures in place and keep the cameras.
