import { useState } from 'react'

export default function FeedbackForm() {
  const [status, setStatus] = useState('idle')

  async function handleSubmit(e) {
    e.preventDefault()
    setStatus('sending')
    const formData = new FormData(e.target)
    
    // Replace with your Web3Forms access key
    formData.append('access_key', 'YOUR_WEB3FORMS_KEY')

    try {
      const res = await fetch('https://api.web3forms.com/submit', {
        method: 'POST',
        body: formData,
      })
      const data = await res.json()
      setStatus(data.success ? 'sent' : 'error')
    } catch (err) {
      setStatus('error')
    }
  }

  return (
    <section className="py-24 px-6 max-w-5xl mx-auto border-t border-white/5">
      <div className="grid md:grid-cols-2 gap-16">
        <div>
          <h2 className="font-display text-3xl mb-4 font-bold tracking-tight text-white">Have feedback?</h2>
          <p className="text-text-muted mb-8">What would you like SyncWave to do next? Have an issue or suggestion? Let me know!</p>
          
          <form onSubmit={handleSubmit} className="flex flex-col gap-4">
            <input type="hidden" name="subject" value="SyncWave feedback" />
            <input
              name="email"
              type="email"
              placeholder="Your email (optional)"
              className="bg-surface rounded-lg px-4 py-3 border border-white/10 focus:outline-none focus:border-accent-main text-white"
            />
            <textarea
              name="message"
              required
              placeholder="Your feedback..."
              rows={5}
              className="bg-surface rounded-lg px-4 py-3 border border-white/10 focus:outline-none focus:border-accent-main text-white"
            />
            <button
              type="submit"
              disabled={status === 'sending'}
              className="bg-accent-sec text-white font-medium rounded-lg py-3 hover:opacity-90 transition disabled:opacity-50"
            >
              {status === 'sending' ? 'Sending…' : 'Send feedback'}
            </button>
            {status === 'sent' && <p className="text-accent-main">Thanks — got it.</p>}
            {status === 'error' && <p className="text-red-400">Something went wrong, try again.</p>}
          </form>
        </div>
        
        <div>
          <h3 className="font-display text-xl mb-4 font-bold text-white">Roadmap</h3>
          <ul className="space-y-4 text-text-muted">
            <li className="flex gap-3">
              <span className="text-accent-main">✓</span> High Performance Mode
            </li>
            <li className="flex gap-3">
              <span className="text-accent-main">✓</span> Independent Device Volume
            </li>
            <li className="flex gap-3">
              <span className="text-accent-main">✓</span> System Tray Controls
            </li>
            <li className="flex gap-3">
              <span className="opacity-50">○</span> Virtual Audio Cable Support
            </li>
            <li className="flex gap-3">
              <span className="opacity-50">○</span> Advanced EQ per Device
            </li>
          </ul>
          
          <div className="mt-8 pt-8 border-t border-white/5">
            <p className="text-sm text-text-muted">
              Or report a bug directly on <a href="https://github.com/NARESH-ASHOK-MALI/SyncWave/issues/new" className="text-accent-main hover:underline" target="_blank" rel="noreferrer">GitHub Issues</a>.
            </p>
          </div>
        </div>
      </div>
    </section>
  )
}
