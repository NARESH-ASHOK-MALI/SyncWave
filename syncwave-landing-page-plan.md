# SyncWave Landing Page — Build Plan

A futuristic, single-purpose landing page: hero → walkthrough → download → feedback.
Built for GitHub Pages (free, matches an open-source project), no backend required.

---

## 1. Setup

```bash
npm create vite@latest syncwave-site -- --template react
cd syncwave-site
npm install three @react-three/fiber @react-three/drei gsap
npm install -D tailwindcss postcss autoprefixer gh-pages
npx tailwindcss init -p
```

## 2. Folder structure

```
syncwave-site/
├── public/
│   └── screenshots/            ← drop your 9 walkthrough JPGs here
├── src/
│   ├── components/
│   │   ├── Hero3D.jsx          ← the signature 3D fan-out visual
│   │   ├── Walkthrough.jsx     ← scroll-driven screenshot sequence
│   │   ├── DownloadCTA.jsx
│   │   ├── FeedbackForm.jsx
│   │   ├── Nav.jsx
│   │   └── Footer.jsx
│   ├── App.jsx
│   ├── main.jsx
│   └── index.css
├── .agents/skills/
│   ├── threejs-scene/SKILL.md
│   ├── scroll-animation/SKILL.md
│   └── static-site-deploy/SKILL.md
├── tailwind.config.js
└── vite.config.js
```

## 3. Design tokens

Chosen specifically for an audio-routing tool — not the default AI-generated cream/terracotta or near-black/acid-green looks.

| Role | Value |
|---|---|
| Background | `#0A0E14` (near-black, blue-shifted, not pure black) |
| Surface / cards | `#121826` |
| Accent (source/waveform) | `#4FE3C1` (teal-cyan) |
| Accent-secondary (device nodes) | `#7C6FF0` (violet) |
| Text | `#E8ECF1` |
| Muted text | `#6B7684` |

Typography:
- Display: **Space Grotesk** (geometric, technical — used with restraint, only for the headline and section eyebrows)
- Body: **Inter**
- Utility/data (latency numbers, version tags): **JetBrains Mono**

Layout concept: full-bleed 3D hero → pinned scroll sequence for the 9 screenshots → download CTA → feedback form as a clean two-column split (form left, short roadmap teaser right).

**Signature element:** SyncWave's whole pitch is "one source, many devices, independent control." So the hero isn't a generic headline+gradient — it's a pulsing central sphere (the audio source) with animated lines reaching out to 3–4 orbiting device nodes, each with its own subtle volume ring and a slight phase offset to suggest per-device latency. That single visual explains the product before anyone reads a word.

## 4. `Hero3D.jsx`

```jsx
import { Canvas, useFrame } from '@react-three/fiber'
import { Line, Sphere } from '@react-three/drei'
import { useRef, useMemo } from 'react'
import * as THREE from 'three'

const DEVICES = [
  { angle: 0, radius: 3.2, color: '#7C6FF0', speed: 1.3, label: 'Bluetooth' },
  { angle: 2.1, radius: 3.6, color: '#4FE3C1', speed: 0.9, label: 'USB' },
  { angle: 4.2, radius: 3.0, color: '#7C6FF0', speed: 1.1, label: 'Wired' },
]

function SourceOrb() {
  const ref = useRef()
  useFrame(({ clock }) => {
    const t = clock.getElapsedTime()
    const scale = 1 + Math.sin(t * 2) * 0.06
    ref.current.scale.setScalar(scale)
  })
  return (
    <Sphere ref={ref} args={[0.55, 48, 48]}>
      <meshStandardMaterial color="#4FE3C1" emissive="#4FE3C1" emissiveIntensity={0.6} />
    </Sphere>
  )
}

function DeviceNode({ angle, radius, color, speed }) {
  const group = useRef()
  const nodeRef = useRef()
  useFrame(({ clock }) => {
    const t = clock.getElapsedTime() * speed + angle
    const x = Math.cos(t) * radius
    const z = Math.sin(t) * radius
    group.current.position.set(x, Math.sin(t * 0.6) * 0.4, z)
    const pulse = 1 + Math.sin(clock.getElapsedTime() * 3 + angle) * 0.15
    nodeRef.current.scale.setScalar(pulse)
  })

  const linePoints = useMemo(
    () => [new THREE.Vector3(0, 0, 0), new THREE.Vector3(0, 0, 0)],
    []
  )

  return (
    <>
      <group ref={group}>
        <Sphere ref={nodeRef} args={[0.18, 24, 24]}>
          <meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.5} />
        </Sphere>
      </group>
    </>
  )
}

export default function Hero3D() {
  return (
    <div className="h-[70vh] w-full">
      <Canvas camera={{ position: [0, 2.5, 8], fov: 50 }}>
        <ambientLight intensity={0.4} />
        <pointLight position={[5, 5, 5]} intensity={1.2} />
        <SourceOrb />
        {DEVICES.map((d, i) => (
          <DeviceNode key={i} {...d} />
        ))}
      </Canvas>
    </div>
  )
}
```

Note: connecting lines between the source and each moving device look best drawn per-frame with `drei`'s `<Line points={...} />` updated in a shared parent `useFrame`, rather than as static geometry — since the device nodes orbit. Wire that up once the base scene is in place; the skill below documents this exact pattern for future edits.

## 5. `Walkthrough.jsx`

```jsx
import { useEffect, useRef } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
gsap.registerPlugin(ScrollTrigger)

const STEPS = [
  { img: 'Step1_LocateDownloadedexefile.jpg', caption: 'Locate the downloaded .exe and run it.' },
  { img: 'Addtobackground.jpg', caption: 'Add SyncWave to the background — it runs quietly.' },
  { img: 'ToggleSecondaryDevicesYouWantToPlay.jpg', caption: 'Toggle the secondary devices you want audio sent to.' },
  { img: 'ConnectAllDevices.jpg', caption: 'Connect all your devices at once.' },
  { img: 'ManageVolumeofsecondaryAudiodevice.jpg', caption: 'Manage volume per secondary device independently.' },
  { img: 'AdvanceMenu.jpg', caption: 'Fine-tune latency and buffering in the Advanced menu.' },
  { img: 'Playing.jpg', caption: 'Play anything — it streams to every connected device.' },
  { img: 'LowCPUConsumption TaskManager.jpg', caption: 'Runs light — barely visible in Task Manager.' },
  { img: 'ExitOrOpenFromSystemTray.jpg', caption: 'Open or exit anytime from the system tray.' },
]

export default function Walkthrough() {
  const containerRef = useRef()

  useEffect(() => {
    const panels = gsap.utils.toArray('.step-panel')
    panels.forEach((panel, i) => {
      gsap.fromTo(
        panel,
        { opacity: 0, y: 40 },
        {
          opacity: 1,
          y: 0,
          scrollTrigger: {
            trigger: panel,
            start: 'top 75%',
            end: 'top 40%',
            scrub: true,
          },
        }
      )
    })
  }, [])

  return (
    <section ref={containerRef} className="py-24 px-6 max-w-3xl mx-auto">
      <h2 className="font-display text-3xl mb-16 text-center">How it works</h2>
      {STEPS.map((s, i) => (
        <div key={i} className="step-panel mb-24">
          <span className="font-mono text-sm text-[#4FE3C1]">
            {String(i + 1).padStart(2, '0')}
          </span>
          <img
            src={`/screenshots/${s.img}`}
            alt={s.caption}
            className="rounded-xl mt-4 mb-4 border border-white/10"
          />
          <p className="text-[#E8ECF1] text-lg">{s.caption}</p>
        </div>
      ))}
    </section>
  )
}
```

Fix the filenames array against your actual files once you drop them into `public/screenshots/` — a couple of yours have spaces/typos (`LowCPUConsumption TaskManager.jpg`, the truncated `ManageVolumeofsecondaryAudiodeviceFro...`), rename them without spaces to avoid URL-encoding headaches.

## 6. `FeedbackForm.jsx` — no backend needed

Uses [Web3Forms](https://web3forms.com) (free) or [Formspree](https://formspree.io) — sign up, get an access key, done.

```jsx
import { useState } from 'react'

export default function FeedbackForm() {
  const [status, setStatus] = useState('idle')

  async function handleSubmit(e) {
    e.preventDefault()
    setStatus('sending')
    const formData = new FormData(e.target)
    formData.append('access_key', 'YOUR_WEB3FORMS_KEY')

    const res = await fetch('https://api.web3forms.com/submit', {
      method: 'POST',
      body: formData,
    })
    const data = await res.json()
    setStatus(data.success ? 'sent' : 'error')
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-col gap-4 max-w-md">
      <input type="hidden" name="subject" value="SyncWave feedback" />
      <input
        name="email"
        type="email"
        placeholder="Your email (optional)"
        className="bg-[#121826] rounded-lg px-4 py-3 border border-white/10 focus:outline-none focus:ring-2 focus:ring-[#4FE3C1]"
      />
      <textarea
        name="message"
        required
        placeholder="What would you like SyncWave to do next?"
        rows={5}
        className="bg-[#121826] rounded-lg px-4 py-3 border border-white/10 focus:outline-none focus:ring-2 focus:ring-[#4FE3C1]"
      />
      <button
        type="submit"
        disabled={status === 'sending'}
        className="bg-[#4FE3C1] text-[#0A0E14] font-medium rounded-lg py-3 hover:opacity-90 transition"
      >
        {status === 'sending' ? 'Sending…' : 'Send feedback'}
      </button>
      {status === 'sent' && <p className="text-[#4FE3C1]">Thanks — got it.</p>}
      {status === 'error' && <p className="text-red-400">Something went wrong, try again.</p>}
    </form>
  )
}
```

Alternative: point a "Report a bug" link straight at `github.com/NARESH-ASHOK-MALI/SyncWave/issues/new` — keeps bug reports public and trackable, separate from casual suggestions.

## 7. Deploy to GitHub Pages

```bash
# vite.config.js — set base to your repo name
export default { base: '/SyncWave-site/' }
```

```bash
npm run build
npx gh-pages -d dist
```

Or automate with a GitHub Actions workflow (`.github/workflows/deploy.yml`) triggered on push to `main` — ask me for that file once the site's structure is settled, it's a five-minute add.

---

## 8. Antigravity skill files

Drop these in `.agents/skills/<name>/SKILL.md` so the agent applies them automatically while you build.

### `.agents/skills/threejs-scene/SKILL.md`
```markdown
---
name: threejs-scene
description: Use when building or editing the React Three Fiber hero scene for the SyncWave landing page — the source-orb-to-device-nodes fan-out visualization.
---

# Three.js Scene Conventions

- Central "source" sphere pulses via useFrame + sine wave on scale, never via CSS.
- Device nodes orbit the source on independent angle/radius/speed per device —
  never synchronize their phase, the point is that devices are independent.
- Connect source to each device with drei <Line>, recomputed every frame from
  live node position (not static geometry) since nodes move.
- Keep total scene under ~3 device nodes + 1 source + lines for perf; more
  needs instancing.
- Dispose of geometries/materials on unmount; check with r3f-perf during dev.
- Respect prefers-reduced-motion: if set, freeze orbit position and only
  pulse opacity, don't disable the scene entirely.
```

### `.agents/skills/scroll-animation/SKILL.md`
```markdown
---
name: scroll-animation
description: Use when adding or editing scroll-triggered reveals for the SyncWave walkthrough section or any other scroll-tied motion on the landing page.
---

# Scroll Animation Conventions

- GSAP + ScrollTrigger only, registered once in the entry component.
- Each walkthrough step: fade + 40px translateY, scrub true, start 'top 75%'
  end 'top 40%' — keep these values consistent across all steps unless a
  step specifically needs a different pace.
- Never stack more than one ScrollTrigger per element without namespacing IDs.
- Clean up ScrollTriggers in a useEffect return to avoid duplicates on
  hot-reload during dev.
```

### `.agents/skills/static-site-deploy/SKILL.md`
```markdown
---
name: static-site-deploy
description: Use when building, testing, or deploying the SyncWave landing page to GitHub Pages.
---

# Deploy Conventions

- vite.config.js base path must match the GitHub Pages repo name exactly.
- Build with `npm run build`, deploy with `npx gh-pages -d dist`.
- Screenshots live in `public/screenshots/` — filenames must have no spaces.
- Verify the download link on the CTA points at the latest GitHub Release
  asset, not a hardcoded old version.
```

---

## What's left for you to fill in

- Drop the 9 screenshots into `public/screenshots/`, rename any with spaces.
- Get a free Web3Forms or Formspree access key and paste it into `FeedbackForm.jsx`.
- Point the download button at your latest GitHub Release `.exe` asset URL.
- Wire up the connecting `<Line>` geometry in `Hero3D.jsx` per the skill note above (needs it recomputed per-frame from live node positions).
