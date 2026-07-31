import { Canvas, useFrame } from '@react-three/fiber'
import { Line, Sphere } from '@react-three/drei'
import { useRef } from 'react'

const DEVICES = [
  { angle: 0, radius: 3.2, color: '#7b2cbf', speed: 1.3, label: 'Bluetooth' }, // accent-purple
  { angle: 2.1, radius: 3.6, color: '#00f0ff', speed: 0.9, label: 'USB' }, // accent-cyan
  { angle: 4.2, radius: 3.0, color: '#7b2cbf', speed: 1.1, label: 'Wired' },
]

function SourceOrb({ sourceRef }) {
  useFrame(({ clock }) => {
    const t = clock.getElapsedTime()
    const scale = 1 + Math.sin(t * 2) * 0.06
    if (sourceRef.current) {
      sourceRef.current.scale.setScalar(scale)
    }
  })
  return (
    <Sphere ref={sourceRef} args={[0.55, 48, 48]}>
      <meshStandardMaterial color="#00f0ff" emissive="#00f0ff" emissiveIntensity={0.8} />
    </Sphere>
  )
}

function DeviceNode({ angle, radius, color, speed, sourceRef }) {
  const group = useRef()
  const nodeRef = useRef()
  const lineRef = useRef()

  useFrame(({ clock }) => {
    const t = clock.getElapsedTime() * speed + angle
    const x = Math.cos(t) * radius
    const z = Math.sin(t) * radius
    const y = Math.sin(t * 0.6) * 0.4
    
    if (group.current) {
      group.current.position.set(x, y, z)
    }
    
    if (nodeRef.current) {
      const pulse = 1 + Math.sin(clock.getElapsedTime() * 3 + angle) * 0.15
      nodeRef.current.scale.setScalar(pulse)
    }

    if (lineRef.current && sourceRef.current) {
      lineRef.current.geometry.setPositions([
        sourceRef.current.position.x, sourceRef.current.position.y, sourceRef.current.position.z,
        x, y, z
      ])
    }
  })

  return (
    <>
      <group ref={group}>
        <Sphere ref={nodeRef} args={[0.18, 24, 24]}>
          <meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.6} />
        </Sphere>
      </group>
      <Line 
        ref={lineRef} 
        points={[[0,0,0], [0,0,0]]} 
        color={color} 
        lineWidth={2} 
        opacity={0.4} 
        transparent 
      />
    </>
  )
}

export default function Hero3D() {
  const sourceRef = useRef()

  return (
    <div className="min-h-[85vh] w-full relative flex items-center justify-center">
      
      <div className="absolute z-10 text-center pointer-events-none select-none flex flex-col items-center top-[20%] w-full">
        <h1 className="font-display text-5xl md:text-8xl font-black tracking-tighter text-white mb-6 uppercase" style={{ textShadow: '0 0 20px rgba(0,240,255,0.3)' }}>
          SYNC<span className="text-accent-cyan">WAVE</span>
        </h1>
        <p className="text-text-muted text-xl md:text-2xl max-w-2xl px-6 mb-12">
          The futuristic audio router. Stream system audio to multiple Bluetooth, USB, and wired devices <span className="text-white font-bold drop-shadow-[0_0_10px_rgba(123,44,191,0.8)]">simultaneously</span>.
        </p>
        
        <a 
          href="https://github.com/NARESH-ASHOK-MALI/SyncWave/releases/download/v2.0.0/SyncWave.exe"
          className="pointer-events-auto inline-flex items-center gap-3 bg-transparent border-2 border-accent-cyan text-accent-cyan font-bold text-lg px-10 py-4 rounded-full shadow-neon-cyan hover:bg-accent-cyan hover:text-bg-base transition-all duration-300 hover:scale-105"
        >
          <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
            <path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path>
            <polyline points="7 10 12 15 17 10"></polyline>
            <line x1="12" y1="15" x2="12" y2="3"></line>
          </svg>
          DOWNLOAD NOW
        </a>
      </div>

      <Canvas camera={{ position: [0, 2, 9], fov: 50 }} className="z-0 opacity-60">
        <ambientLight intensity={0.2} />
        <pointLight position={[5, 5, 5]} intensity={1.5} />
        <SourceOrb sourceRef={sourceRef} />
        {DEVICES.map((d, i) => (
          <DeviceNode key={i} {...d} sourceRef={sourceRef} />
        ))}
      </Canvas>
    </div>
  )
}
