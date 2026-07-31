import { useState } from 'react'

export default function FeedbackForm() {
  const [status, setStatus] = useState('idle')

  async function handleSubmit(e) {
    e.preventDefault()
    setStatus('sending')
    const formData = new FormData(e.target)
    
    // Replace with your Web3Forms access key
    formData.append('access_key', 'bb09276a-5dfd-4898-837f-c5cfc7f62a70')

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
    <section className="py-32 px-6 max-w-4xl mx-auto relative z-10">
      <div className="bg-surface/50 backdrop-blur-xl border border-surface-border rounded-3xl p-10 md:p-16 shadow-glass relative overflow-hidden">
        
        {/* Decorative corner glows */}
        <div className="absolute top-0 left-0 w-32 h-32 bg-accent-cyan/10 blur-[50px] rounded-full pointer-events-none" />
        <div className="absolute bottom-0 right-0 w-32 h-32 bg-accent-purple/10 blur-[50px] rounded-full pointer-events-none" />

        <div className="text-center mb-12 relative z-10">
          <h2 className="font-display text-3xl md:text-4xl font-bold tracking-tight text-white mb-4">
            Feature Request & Feedback
          </h2>
          <p className="text-text-muted text-lg">Help shape the future of SyncWave. Send your thoughts directly.</p>
        </div>
        
        <form onSubmit={handleSubmit} className="flex flex-col gap-6 relative z-10">
          <input type="hidden" name="subject" value="SyncWave Feedback" />
          
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <input
              name="name"
              type="text"
              required
              placeholder="Name"
              className="w-full bg-bg-base/50 text-white px-5 py-4 rounded-xl border border-transparent outline-none transition-all duration-300 focus:bg-bg-base focus:border-accent-cyan focus:shadow-neon-cyan placeholder-text-muted/50"
            />
            <input
              name="email"
              type="email"
              required
              placeholder="Email"
              className="w-full bg-bg-base/50 text-white px-5 py-4 rounded-xl border border-transparent outline-none transition-all duration-300 focus:bg-bg-base focus:border-accent-cyan focus:shadow-neon-cyan placeholder-text-muted/50"
            />
          </div>
          
          <textarea
            name="message"
            required
            placeholder="Your Suggestion..."
            rows={5}
            className="w-full bg-bg-base/50 text-white px-5 py-4 rounded-xl border border-transparent outline-none transition-all duration-300 focus:bg-bg-base focus:border-accent-cyan focus:shadow-neon-cyan placeholder-text-muted/50 resize-none"
          />
          
          <button
            type="submit"
            disabled={status === 'sending'}
            className="mt-4 self-center bg-transparent border-2 border-accent-purple text-accent-purple font-bold px-12 py-4 rounded-full shadow-neon-purple hover:bg-accent-purple hover:text-white transition-all duration-300 hover:scale-105 disabled:opacity-50 disabled:hover:scale-100 disabled:hover:bg-transparent disabled:hover:text-accent-purple"
          >
            {status === 'sending' ? 'SENDING...' : 'SEND TO DEVELOPER'}
          </button>
          
          {status === 'sent' && <p className="text-accent-cyan text-center font-medium mt-4">Thank you! Your feedback has been sent.</p>}
          {status === 'error' && <p className="text-red-400 text-center font-medium mt-4">Something went wrong. Please try again.</p>}
        </form>
      </div>
    </section>
  )
}
