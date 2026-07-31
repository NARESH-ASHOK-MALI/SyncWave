import Hero3D from './components/Hero3D'
import Walkthrough from './components/Walkthrough'
import DownloadCTA from './components/DownloadCTA'
import FeedbackForm from './components/FeedbackForm'

function App() {
  return (
    <div className="min-h-screen bg-bg-base text-text-main selection:bg-accent-cyan selection:text-bg-base overflow-x-hidden relative">
      {/* Background gradients for futuristic feel */}
      <div className="fixed inset-0 z-0 pointer-events-none opacity-20">
        <div className="absolute top-[-20%] left-[-10%] w-[50%] h-[50%] rounded-full bg-accent-cyan blur-[120px]" />
        <div className="absolute bottom-[-20%] right-[-10%] w-[40%] h-[40%] rounded-full bg-accent-purple blur-[120px]" />
      </div>

      <nav className="fixed top-0 left-0 right-0 z-50 py-4 px-8 flex justify-between items-center bg-bg-base/60 backdrop-blur-md border-b border-surface-border shadow-glass">
        <div className="font-display font-bold text-xl tracking-tight text-white flex items-center gap-3">
          <img src="./logo.png" alt="SyncWave" className="w-8 h-8 rounded-lg shadow-neon-cyan" />
          SyncWave
        </div>
        <a 
          href="https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/download/v2.0.0/SyncWave.exe"
          className="text-sm font-medium border border-accent-cyan text-accent-cyan hover:bg-accent-cyan hover:text-bg-base hover:shadow-neon-cyan px-5 py-2 rounded-full transition-all duration-300"
        >
          Download
        </a>
      </nav>

      <main className="relative z-10 pt-20">
        <Hero3D />
        <Walkthrough />
        <DownloadCTA />
        <FeedbackForm />
      </main>

      <footer className="relative z-10 py-12 text-center text-sm text-text-muted border-t border-surface-border bg-bg-base/80 backdrop-blur-lg">
        <p>&copy; {new Date().getFullYear()} SyncWave. Open Source.</p>
      </footer>
    </div>
  )
}

export default App
