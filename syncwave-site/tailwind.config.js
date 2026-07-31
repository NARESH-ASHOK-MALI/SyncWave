/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        'bg-base': '#0a0a0a',
        'surface': 'rgba(255, 255, 255, 0.03)',
        'surface-border': 'rgba(255, 255, 255, 0.08)',
        'accent-cyan': '#00f0ff',
        'accent-purple': '#7b2cbf',
        'text-main': '#f8f9fa',
        'text-muted': '#a0aab2'
      },
      fontFamily: {
        'display': ['"Space Grotesk"', 'sans-serif'],
        'sans': ['Inter', 'sans-serif'],
        'mono': ['"JetBrains Mono"', 'monospace']
      },
      boxShadow: {
        'neon-cyan': '0 0 15px rgba(0, 240, 255, 0.5)',
        'neon-purple': '0 0 15px rgba(123, 44, 191, 0.5)',
        'glass': '0 8px 32px 0 rgba(0, 0, 0, 0.3)',
      }
    },
  },
  plugins: [],
}
