/** @type {import('tailwindcss').Config} */
export default {
  content: [
    "./index.html",
    "./src/**/*.{js,ts,jsx,tsx}",
  ],
  theme: {
    extend: {
      colors: {
        'bg-base': '#0A0E14',
        'surface': '#121826',
        'accent-main': '#4FE3C1',
        'accent-sec': '#7C6FF0',
        'text-main': '#E8ECF1',
        'text-muted': '#6B7684'
      },
      fontFamily: {
        'display': ['"Space Grotesk"', 'sans-serif'],
        'sans': ['Inter', 'sans-serif'],
        'mono': ['"JetBrains Mono"', 'monospace']
      }
    },
  },
  plugins: [],
}
