---
name: syncwave-performance-mode
description: Use this whenever working on SyncWave's WasapiOut buffer size, silence pre-fill, event-driven vs polling capture, or a "High Performance Mode" / latency toggle in Advanced settings. Trigger for anything mentioning "30ms", "15ms", "buffer latency", "event-driven capture", "SetEventHandle", or "performance mode" in this repo. Consult this BEFORE changing ProcessLoopbackCaptureService's capture loop — that code has a documented fragile history (see below) and needs the same careful, isolated verification the original capture work required.
---

# SyncWave — High Performance Mode

## Project context

Current default latency path: WasapiOut buffer ~30ms, a silence
pre-fill on sync start, and polling-based capture in
`ProcessLoopbackCaptureService.CaptureLoop()` (a `Thread.Sleep(5)` loop
checking `GetNextPacketSize()`).

Goal: add an opt-in "High Performance Mode (requires strong CPU)"
toggle in Advanced settings that, when enabled, applies three changes
together to bring total latency down to roughly 15-20ms:

1. WasapiOut buffer: 30ms → 15ms
2. Remove the silence pre-fill at sync start
3. Switch capture from polling (`Thread.Sleep(5)`) to event-driven
   (`IAudioClient.SetEventHandle` + a wait-handle-based loop instead of
   sleep-polling)

## Why this needs extra care — read before touching CaptureLoop()

`ProcessLoopbackCaptureService.cs` already went through a difficult,
multi-round debugging history in this project: an STA/MTA COM apartment
mismatch that took several iterations to correctly diagnose (see
`syncwave-audio-independence` skill for the full account), and an
E_NOINTERFACE saga before that. This is proven-fragile code. Do not
touch `CaptureLoop()` casually or bundle its change in with the other
two — it gets its own isolated step, its own standalone test, and its
own explicit before/after verification, the same rigor the original
capture work required.

## Decisions made

- **This is opt-in, not a silent default change.** All three changes
  apply only when the user explicitly enables "High Performance Mode"
  in Advanced settings — the default 30ms/pre-fill/polling path remains
  the safe, unchanged behavior for anyone who doesn't opt in.
- **One toggle controls all three changes together** — not three
  separate toggles. The user-facing framing is "requires strong CPU,"
  not three individually-explained tradeoffs.
- **Phasing (in this order, each independently verified before the next):**
  1. Buffer 30ms→15ms alone, gated behind the toggle. Lowest risk,
     easiest to verify (listen for crackle under normal use).
  2. Remove silence pre-fill, gated behind the same toggle. Also low
     risk — only affects the first ~15ms at sync start, not steady-state
     playback.
  3. Event-driven capture, gated behind the same toggle, done LAST and
     verified most carefully. This is the one touching
     `CaptureLoop()`'s core loop. Before wiring this into the real
     toggle, prove it works in isolation first — same pattern as the
     original PoC: a standalone test confirming the event-driven loop
     still captures correctly, doesn't miss packets, doesn't
     deadlock/hang on Stop(), and doesn't reintroduce any of the
     COM/threading issues from the capture's original history.
- **Toggling High Performance Mode requires an active resync** — since
  buffer size and capture mode are set at stream-initialization time,
  not adjustable on a live stream. If the user flips the toggle while
  Start Sync is already active, decide and implement one clear behavior:
  either (a) apply on next Start Sync only, with a note in the UI, or
  (b) automatically stop and restart sync when toggled while active.
  Pick (a) unless told otherwise — it's simpler and less surprising.

## Files this will touch

- `Core/AudioOutputService.cs` — WasapiOut buffer duration, silence
  pre-fill logic.
- `Core/ProcessLoopbackCaptureService.cs` — capture loop, ONLY in the
  final phase, in isolation.
- `ViewModels/MainViewModel.cs` / Advanced settings UI — the toggle
  itself, following the same pattern as the 200% volume boost toggle
  (persisted the same way, same Advanced section).

## Files to leave alone

- Everything already stable from the previous two branches — this
  phase only touches the specific buffer/pre-fill/capture-loop pieces
  described above. No tray/flyout/theme changes, no volume-independence
  architecture changes.
