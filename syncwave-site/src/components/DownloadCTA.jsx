export default function DownloadCTA() {
  return (
    <section className="py-24 px-6 text-center max-w-3xl mx-auto relative z-10">
      <div className="relative inline-block mb-6">
        <div className="absolute inset-0 bg-accent-cyan blur-[60px] opacity-20 rounded-full" />
        <h2 className="font-display text-4xl md:text-5xl font-bold tracking-tight text-white relative">
          Ready to sync your audio?
        </h2>
      </div>
      <p className="text-text-muted text-xl mb-12 max-w-xl mx-auto">
        Download SyncWave for free. No installation, no sign-up — just run the executable and take control.
      </p>
      
      <a 
        href="https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/download/v2.0.0/SyncWave.exe"
        className="inline-flex items-center gap-3 bg-accent-cyan text-bg-base font-bold text-lg px-10 py-5 rounded-full hover:scale-105 transition-all duration-300 shadow-neon-cyan hover:shadow-[0_0_30px_rgba(0,240,255,0.8)]"
      >
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
        DOWNLOAD V2.0.0
      </a>
      
      <div className="mt-12 flex justify-center gap-8 text-sm text-text-muted">
        <a href="https://github.com/NARESH-ASHOK-MALI/SyncWave" className="hover:text-accent-cyan transition-colors" target="_blank" rel="noreferrer">View on GitHub</a>
        <span className="opacity-50">&bull;</span>
        <a href="https://github.com/NARESH-ASHOK-MALI/SyncWave/blob/main/LICENSE" className="hover:text-accent-cyan transition-colors" target="_blank" rel="noreferrer">MIT License</a>
      </div>
    </section>
  )
}
