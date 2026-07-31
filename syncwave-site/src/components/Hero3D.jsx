import { Canvas, useFrame } from '@react-three/fiber'
import { Line, Sphere } from '@react-three/drei'
import { useRef } from 'react'

const DEVICES = [
  { angle: 0, radius: 3.2, color: '#7C6FF0', speed: 1.3, label: 'Bluetooth' },
  { angle: 2.1, radius: 3.6, color: '#4FE3C1', speed: 0.9, label: 'USB' },
  { angle: 4.2, radius: 3.0, color: '#7C6FF0', speed: 1.1, label: 'Wired' },
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
      <meshStandardMaterial color="#4FE3C1" emissive="#4FE3C1" emissiveIntensity={0.6} />
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
      // Update Line2 geometry positions directly for performance
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
          <meshStandardMaterial color={color} emissive={color} emissiveIntensity={0.5} />
        </Sphere>
      </group>
      <Line 
        ref={lineRef} 
        points={[[0,0,0], [0,0,0]]} 
        color={color} 
        lineWidth={1.5} 
        opacity={0.3} 
        transparent 
      />
    </>
  )
}

export default function Hero3D() {
  const sourceRef = useRef()

  return (
    <div className="h-[70vh] w-full bg-bg-base relative flex items-center justify-center">
      <div className="absolute inset-0 z-0 pointer-events-none" style={{
        background: 'radial-gradient(circle at center, rgba(79, 227, 193, 0.08) 0%, transparent 60%)'
      }}></div>
      
      <div className="absolute z-10 text-center pointer-events-none select-none flex flex-col items-center mt-32">
        <h1 className="font-display text-5xl md:text-7xl font-bold tracking-tight text-white mb-6">
          One source.<br />
          <span className="text-accent-main">Many devices.</span>
        </h1>
        <p className="text-text-muted text-lg md:text-xl max-w-xl px-6">
          Stream system audio to multiple Bluetooth, USB, and wired devices simultaneously — with real-time per-device volume & latency control.
        </p>
      </div>

      <Canvas camera={{ position: [0, 2.5, 8], fov: 50 }} className="z-0">
        <ambientLight intensity={0.4} />
        <pointLight position={[5, 5, 5]} intensity={1.2} />
        <SourceOrb sourceRef={sourceRef} />
        {DEVICES.map((d, i) => (
          <DeviceNode key={i} {...d} sourceRef={sourceRef} />
        ))}
      </Canvas>
    </div>
  )
}
