export default function DownloadCTA() {
  return (
    <section className="py-24 px-6 text-center max-w-3xl mx-auto border-t border-white/5">
      <h2 className="font-display text-4xl mb-6 font-bold tracking-tight text-white">Ready to sync your audio?</h2>
      <p className="text-text-muted text-xl mb-12">
        Download SyncWave for free. No installation, no sign-up — just run the exe.
      </p>
      
      <a 
        href="https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/download/v2.0.0/SyncWave.exe"
        className="inline-flex items-center gap-2 bg-accent-main text-bg-base font-semibold px-8 py-4 rounded-xl hover:scale-105 transition-transform duration-200"
      >
        <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
          <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
          <polyline points="7 10 12 15 17 10"></polyline>
          <line x1="12" y1="15" x2="12" y2="3"></line>
        </svg>
        Download v2.0.0
      </a>
      
      <div className="mt-8 flex justify-center gap-6 text-sm text-text-muted">
        <a href="https://github.com/NARESH-ASHOK-MALI/SyncWave" className="hover:text-white transition-colors" target="_blank" rel="noreferrer">GitHub</a>
        <span>&bull;</span>
        <a href="https://github.com/NARESH-ASHOK-MALI/SyncWave/blob/main/LICENSE" className="hover:text-white transition-colors" target="_blank" rel="noreferrer">MIT License</a>
      </div>
    </section>
  )
}
