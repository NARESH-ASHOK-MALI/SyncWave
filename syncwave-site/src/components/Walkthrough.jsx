import { useEffect, useRef } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
gsap.registerPlugin(ScrollTrigger)

const STEPS = [
  { img: 'Step1_LocateDownloadedexefile.jpg', caption: 'Locate the downloaded .exe and run it. No installation required.' },
  { img: 'Addtobackground.jpg', caption: 'Add SyncWave to the background — it runs quietly in the system tray.' },
  { img: 'ConnectAllDevices.jpg', caption: 'Connect all your audio devices at once.' },
  { img: 'ToggleSecondaryDevicesYouWantToPlay.jpg', caption: 'Toggle the secondary devices you want audio sent to.' },
  { img: 'ManageVolumeofsecondaryAudiodeviceFromSystemTray.jpg', caption: 'Manage volume per secondary device independently from the tray flyout.' },
  { img: 'AdvanceMenu.jpg', caption: 'Fine-tune latency and buffering in the Advanced menu.' },
  { img: 'Playing.jpg', caption: 'Play anything — it streams to every connected device seamlessly.' },
  { img: 'LowCPUConsumptionTaskManager.jpg', caption: 'Runs extremely light — barely visible in Task Manager.' },
  { img: 'ExitOrOpenFromSystemTray.jpg', caption: 'Open or exit anytime directly from the system tray.' },
]

export default function Walkthrough() {
  const containerRef = useRef()

  useEffect(() => {
    // Clean up any old ScrollTriggers from HMR
    ScrollTrigger.getAll().forEach(t => t.kill());

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
    
    return () => {
      ScrollTrigger.getAll().forEach(t => t.kill());
    }
  }, [])

  return (
    <section ref={containerRef} className="py-24 px-6 max-w-4xl mx-auto">
      <h2 className="font-display text-4xl mb-24 text-center tracking-tight text-white">How it works</h2>
      <div className="space-y-32">
        {STEPS.map((s, i) => (
          <div key={i} className="step-panel">
            <span className="font-mono text-lg text-accent-main mb-2 block">
              {String(i + 1).padStart(2, '0')}
            </span>
            <img
              src={`./screenshots/${s.img}`}
              alt={s.caption}
              className="w-full rounded-xl border border-white/10 shadow-2xl shadow-black/50"
            />
            <p className="text-text-main text-xl mt-6 text-center">{s.caption}</p>
          </div>
        ))}
      </div>
    </section>
  )
}
