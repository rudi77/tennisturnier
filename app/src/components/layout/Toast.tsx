import { useToast } from '../../hooks/useToast'

export function Toast() {
  const { message, tone } = useToast()
  if (!message) return null

  return (
    <div className="md-toast" role="status" aria-live="polite">
      <span
        style={{
          width: 8,
          height: 8,
          flex: 'none',
          borderRadius: 'var(--radius-pill)',
          background: tone === 'error' ? 'var(--call-400)' : 'var(--acc)',
        }}
      />
      {message}
    </div>
  )
}
