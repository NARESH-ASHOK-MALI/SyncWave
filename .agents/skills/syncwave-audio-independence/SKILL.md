---
name: syncwave-audio-independence
description: Use this whenever working on SyncWave's audio capture, volume, or "Source device" logic — specifically AudioCaptureService.cs, AudioOutputService.cs, VolumeWaveProvider.cs, or the StartSync/StopSync logic in MainViewModel.cs. Also trigger this for any task mentioning "process loopback", "WASAPI loopback", "Source device volume", "echo prevention", "per-device volume independence", or "COM interop for audio capture" in this repo. This skill contains the agreed-upon architecture and a phased implementation plan — consult it BEFORE proposing a different approach (e.g. a virtual audio cable/driver, or re-locking the Source device to 100% as a "fix") since those directions have already been deliberately rejected for this project.
---

# SyncWave — Audio Independence Refactor

## Project context

SyncWave (C#/.NET 8, WPF, NAudio, WASAPI) streams system audio to multiple
output devices simultaneously with per-device volume and latency control.

**Current pipeline:**
```
System Audio → WASAPI Loopback (default device) → Capture Buffer
             → Latency Delay (per device) → Volume Scale (per device)
             → WasapiOut → Device
```

One connected device is auto-detected as the "Source" (Windows' current
default render device) and is *skipped* as an output target — this is
existing "echo prevention" logic, so the same audio doesn't play twice
through the same physical device.

## The bug this refactor exists to fix

`WasapiLoopbackCapture` taps audio **after** Windows has already applied
the Source device's endpoint (master) volume. Every other synced device's
volume is just a multiplier applied on top of that already-scaled buffer.
Consequence: if the Source device's Windows volume is at 0%, the captured
buffer is already silence, and no per-device gain downstream can recover
audio for any other device. This breaks the entire premise of
"independent per-device volume."

A prior patch locked the Source device's Windows volume to 100% while
syncing (mirroring what `AddDevice()` already did for other output
devices). That patch is a safety net, **not** the real fix — it stops the
Source device from silently zeroing everything, but the Source device
still has no independent app-side volume slider of its own, because
scaling the shared captured buffer to give it one would reintroduce the
exact same bug for every other device.

## Decision already made — do not relitigate this

We are implementing **Option B: Windows Process Loopback Capture**
(`AUDIOCLIENT_ACTIVATION_PARAMS` with `ProcessLoopback` mode, via
`ActivateAudioInterfaceAsync`), capturing the whole system's audio at the
session level — **upstream of any device's endpoint volume**.

**Explicitly rejected, do not suggest instead:**
- A virtual audio cable (e.g. VB-CABLE) as the capture source. Rejected
  because it requires users to install a third-party driver, which
  conflicts with SyncWave's "just run the exe, no install needed"
  design goal.
- Keeping the Source-device-lock-to-100% patch as the permanent solution.
  It's a stopgap already in the codebase; the goal of this refactor is to
  make it unnecessary by removing the Source/output asymmetry entirely.

Once process-loopback capture is in place, capture itself is fully
independent of every device's Windows volume — this part IS fully
symmetric and required no exceptions.

**However, one exception remains, and it is permanent, not a stopgap:**
whichever device is currently Windows' default render device will always
receive system audio *natively* (Path 1) regardless of anything SyncWave
does, because that's simply what being the OS default device means.
Once that device is *also* sent SyncWave's own re-rendered copy via
WasapiOut (Path 2), both paths physically converge on the same hardware,
offset by processing latency — an audible echo. This is the real cost of
choosing process-loopback capture over a virtual audio cable (Option A):
a virtual cable would have made every device symmetric, since nothing
would play natively anywhere; without one, the currently-default device
cannot be given its own independent WasapiOut render without echoing.

There is no driver-free way around this — muting that device's Windows
endpoint volume to silence just Path 1 doesn't work (endpoint volume is
the final stage before hardware and mutes Path 2 too), and muting other
apps' individual sessions doesn't work either (process-loopback capture
taps that same session mix, so it would silence audio for every other
synced device too).

**Final, correct architecture:** every device *except* whichever one is
currently Windows' default gets full symmetric treatment — own
`WasapiOut`, own `VolumeWaveProvider`, own independent app slider, no
skip logic, no volume lock (unnecessary now — capture doesn't depend on
any device's Windows volume). The currently-default device alone is
skipped from `AddDevice()` to prevent the echo, and its volume remains
controlled solely by the Windows taskbar slider — not as a workaround to
revisit, but as the genuine ceiling of what's achievable without adding
a virtual-driver dependency, which was explicitly ruled out earlier in
this project. Do not attempt to eliminate this exception without first
revisiting the Option A (virtual cable) vs. B (process loopback)
decision itself.

## Implementation plan (phased)

Work through these roughly in order. Each phase should be independently
testable before moving to the next.

1. **Raw COM interop layer** — `ActivateAudioInterfaceAsync` from
   `mmdevapi.dll`, the `AUDIOCLIENT_ACTIVATION_PARAMS` struct
   (`ProcessLoopback` mode, `TargetProcessId = 0` for whole-system,
   pick `INCLUDE`/`EXCLUDE_TARGET_PROCESS_TREE` deliberately re:
   excluding SyncWave's own process), and the
   `IActivateAudioInterfaceAsyncOperation` completion callback. Isolate
   this in its own class — no NAudio involved yet.
   **Build and test this in complete isolation first** (a throwaway
   console test that just captures and logs audio) before touching any
   existing SyncWave file. COM struct marshaling and activation
   callbacks are the highest-risk part of this whole refactor — get them
   right on their own before wiring anything in.

2. **Drop-in capture wrapper** — a new class exposing the *exact* public
   surface `AudioCaptureService` already has: `Start()`, `Stop()`,
   `DataAvailable` event, `CaptureFormatAvailable` event, `CaptureFormat`
   property. Internally uses the `IAudioClient` from step 1 instead of
   `WasapiLoopbackCapture`. Matching the interface exactly means nothing
   downstream needs to change.

3. **Swap the capture implementation** — replace the
   `AudioCaptureService` instantiation in `MainViewModel` with the new
   class. `DistributeAudio`, `VolumeWaveProvider`, `WasapiOut` should
   need zero changes.

4. **Remove the Source special-case** — in `MainViewModel.StartSync()`,
   delete the `if (device.IsDefaultDevice)` skip branch entirely. Every
   selected device goes through the normal `AddDevice()` path. In
   `AudioOutputService`, `LockSourceVolume()` and the echo-prevention
   skip logic become dead code — delete them. In the UI, retire the
   "🔈 Source" label and "no echo" status text.

5. **Decide the volume-locking policy** — now that capture doesn't
   depend on any device's Windows volume, choose: (a) keep forcing every
   device's endpoint volume to 100% so the app slider is the sole source
   of truth (recommended — matches existing B/C behavior and the
   original goal), or (b) read each device's current OS volume once at
   sync-start as the slider's initial value and let it diverge from
   there. Default to (a) unless told otherwise.

6. **Format/edge-case handling** — process-loopback capture may return a
   different mix format (sample rate/bit depth/channels) than
   `WasapiLoopbackCapture` did. Confirm `SetSourceFormat` and the
   `BufferedWaveProvider`s tolerate it, or add resampling. Add a clear
   error path for activation failure — there's no "default device"
   fallback to lean on anymore.

7. **Test the failure modes this was built to fix** — see checklist
   below.

## Testing checklist

- [ ] Dropping any one device's slider to 0 has no effect on the others.
- [ ] Changing the (former) Source device's Windows volume via taskbar
      or media keys mid-session has **zero** audible effect on any
      device.
- [ ] No feedback/echo loop, now that the former Source device renders
      through the app's own `WasapiOut` like every other device.
- [ ] Unplug/reconnect of the former Source device mid-session behaves
      the same as the existing reconnection logic already does for
      other devices.

## Files this touches

- `Core/AudioCaptureService.cs` — replaced/rewritten (or a new sibling
  class swapped in, per step 2)
- `Core/AudioOutputService.cs` — remove `LockSourceVolume` and
  Source-skip logic (step 4)
- `ViewModels/MainViewModel.cs` — remove the `IsDefaultDevice` skip
  branch in `StartSync()` (step 4)
- `Models/AudioDeviceModel.cs` — `IsDefaultDevice`/`DeviceType` "Source"
  labeling likely becomes cosmetic-only or removable
- Views (XAML) — remove "🔈 Source" / "no echo" UI text

## Files to leave alone

- `Core/LatencyManager.cs` and `Core/VolumeWaveProvider.cs` — the
  per-device delay and volume-scaling logic is already correct and
  device-symmetric; this refactor only changes *what* gets captured and
  *how many* devices go through the existing output pipeline, not the
  pipeline itself.
- `Utils/DeviceProfileManager.cs` — profile persistence format doesn't
  need to change.

## Gotchas to watch for

- The `ActivateAudioInterfaceAsync` completion callback fires on a COM
  thread, not necessarily one safe for direct UI or NAudio buffer
  interaction — marshal appropriately before touching shared state.
- Struct packing/marshaling for `AUDIOCLIENT_ACTIVATION_PARAMS` is easy
  to get subtly wrong (field order, `LayoutKind`, GUID formatting) —
  errors here tend to surface as silent activation failure or garbage
  audio, not a clean exception.
- Minimum OS requirement is Windows 10 2004 (build 19041+) for process
  loopback — worth a README note, though almost certainly a non-issue
  given the existing .NET 8 / Windows 10-11 target.