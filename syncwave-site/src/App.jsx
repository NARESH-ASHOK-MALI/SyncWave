import Hero3D from './components/Hero3D'
import Walkthrough from './components/Walkthrough'
import DownloadCTA from './components/DownloadCTA'
import FeedbackForm from './components/FeedbackForm'

function App() {
  return (
    <div className="min-h-screen bg-bg-base text-text-main selection:bg-accent-main selection:text-bg-base overflow-x-hidden">
      <nav className="absolute top-0 left-0 right-0 z-50 py-6 px-8 flex justify-between items-center">
        <div className="font-display font-bold text-xl tracking-tight text-white flex items-center gap-2">
          <div className="w-6 h-6 rounded-md bg-accent-main"></div>
          SyncWave
        </div>
        <a 
          href="https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/download/v2.0.0/SyncWave.exe"
          className="text-sm font-medium border border-white/20 hover:border-accent-main hover:text-accent-main px-4 py-2 rounded-full transition-colors"
        >
          Download
        </a>
      </nav>

      <main>
        <Hero3D />
        <Walkthrough />
        <DownloadCTA />
        <FeedbackForm />
      </main>

      <footer className="py-8 text-center text-sm text-text-muted">
        <p>&copy; {new Date().getFullYear()} SyncWave. Open Source.</p>
      </footer>
    </div>
  )
}

export default App
