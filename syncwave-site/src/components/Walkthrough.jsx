import { useEffect, useRef, useState } from 'react'
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
  const [activeStep, setActiveStep] = useState(0)

  useEffect(() => {
    // Clean up any old ScrollTriggers from HMR
    ScrollTrigger.getAll().forEach(t => t.kill());

    const steps = gsap.utils.toArray('.step-text')
    
    steps.forEach((step, i) => {
      ScrollTrigger.create({
        trigger: step,
        start: 'top 60%',
        end: 'bottom 60%',
        onToggle: self => {
          if (self.isActive) {
            setActiveStep(i)
          }
        }
      })
    })
    
    return () => {
      ScrollTrigger.getAll().forEach(t => t.kill());
    }
  }, [])

  return (
    <section ref={containerRef} className="py-24 px-6 max-w-6xl mx-auto">
      <h2 className="font-display text-4xl mb-12 text-center tracking-tight text-white">How it works</h2>
      
      <div className="flex flex-col md:flex-row gap-16 relative items-start">
        {/* Left side: scrolling text */}
        <div className="w-full md:w-5/12 pb-[30vh]">
          {STEPS.map((s, i) => (
            <div 
              key={i} 
              className={`step-text min-h-[40vh] flex flex-col justify-center transition-opacity duration-500 ${activeStep === i ? 'opacity-100' : 'opacity-20'}`}
            >
              <span className="font-mono text-xl text-accent-main mb-4 block">
                {String(i + 1).padStart(2, '0')}
              </span>
              <p className="text-text-main text-2xl font-medium leading-relaxed">{s.caption}</p>
            </div>
          ))}
        </div>

        {/* Right side: sticky image container */}
        <div className="w-full md:w-7/12 sticky top-[20vh] h-[55vh] rounded-2xl border border-white/10 overflow-hidden shadow-2xl bg-surface">
          {STEPS.map((s, i) => (
            <img
              key={i}
              src={`./screenshots/${s.img}`}
              alt={s.caption}
              className={`absolute inset-0 w-full h-full object-cover transition-opacity duration-700 ease-in-out ${activeStep === i ? 'opacity-100 z-10' : 'opacity-0 z-0'}`}
            />
          ))}
        </div>
      </div>
    </section>
  )
}
