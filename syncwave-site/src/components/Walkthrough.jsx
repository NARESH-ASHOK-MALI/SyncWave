export default function Walkthrough() {
  const screenshots = [
    { src: 'Step1_LocateDownloadedexefile.jpg', alt: 'Download and run without installation', span: 'md:col-span-2 md:row-span-1' },
    { src: 'ConnectAllDevices.jpg', alt: 'Connect all devices instantly', span: 'md:col-span-1 md:row-span-1' },
    { src: 'ToggleSecondaryDevicesYouWantToPlay.jpg', alt: 'Toggle any secondary device', span: 'md:col-span-1 md:row-span-2' },
    { src: 'ManageVolumeofsecondaryAudiodeviceFromSystemTray.jpg', alt: 'Manage volume directly from the system tray', span: 'md:col-span-1 md:row-span-1' },
    { src: 'Playing.jpg', alt: 'Play audio across all outputs', span: 'md:col-span-1 md:row-span-1' },
    { src: 'AdvanceMenu.jpg', alt: 'Advanced settings for perfect sync', span: 'md:col-span-2 md:row-span-1' },
    { src: 'ExitOrOpenFromSystemTray.jpg', alt: 'Minimize to tray', span: 'md:col-span-1 md:row-span-1' },
    { src: 'Addtobackground.jpg', alt: 'Runs quietly in the background', span: 'md:col-span-1 md:row-span-1' },
    { src: 'LowCPUConsumptionTaskManager.jpg', alt: 'Ultra low CPU footprint', span: 'md:col-span-1 md:row-span-1' },
  ]

  return (
    <section className="py-24 px-6 max-w-6xl mx-auto relative z-10">
      <div className="text-center mb-16">
        <h2 className="font-display text-4xl md:text-5xl font-bold tracking-tight text-white mb-4">
          Experience <span className="text-accent-purple" style={{ textShadow: '0 0 20px rgba(123,44,191,0.5)' }}>SyncWave</span>
        </h2>
        <p className="text-text-muted text-lg">A frictionless interface for controlling complex audio pipelines.</p>
      </div>

      <div className="grid grid-cols-1 md:grid-cols-3 auto-rows-[250px] gap-6">
        {screenshots.map((s, i) => (
          <div 
            key={i} 
            className={`group relative rounded-2xl overflow-hidden border border-surface-border bg-surface backdrop-blur-md transition-all duration-500 hover:border-accent-cyan hover:shadow-neon-cyan ${s.span}`}
          >
            <div className="absolute inset-0 bg-gradient-to-t from-bg-base/90 via-bg-base/20 to-transparent z-10 opacity-60 group-hover:opacity-40 transition-opacity duration-500" />
            
            <img
              src={`./screenshots/${s.src}`}
              alt={s.alt}
              className="w-full h-full object-cover object-top opacity-80 group-hover:opacity-100 group-hover:scale-105 transition-all duration-700"
            />
            
            <div className="absolute bottom-0 left-0 p-6 z-20 transform translate-y-2 group-hover:translate-y-0 transition-transform duration-500">
              <p className="text-white font-medium text-lg shadow-sm drop-shadow-md">
                {s.alt}
              </p>
            </div>
          </div>
        ))}
      </div>
    </section>
  )
}
