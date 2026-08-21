import { fireEvent, screen, waitFor } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { Toast } from '../layout/Toast'
import { renderWithProviders } from '../../test/render'
import { ShareLink } from './ShareLink'

const LINK = 'http://localhost:5000/?r=tok-abcdef'

function aufbau(over: Partial<Parameters<typeof ShareLink>[0]> = {}) {
  return renderWithProviders(
    <>
      <ShareLink
        url={LINK}
        label="Anmeldelink kopieren"
        shareTitle="Clubmeisterschaft 2026"
        shareText="Jetzt melden"
        copiedMessage="Anmeldelink kopiert."
        {...over}
      />
      <Toast />
    </>,
    { workspace: null },
  )
}

/** Die Zwischenablage, die es im sicheren Kontext gibt — und sonst nicht. */
function mitClipboard(writeText: () => Promise<void>) {
  Object.defineProperty(navigator, 'clipboard', {
    configurable: true,
    value: { writeText: vi.fn(writeText) },
  })
}

function ohneClipboard() {
  Object.defineProperty(navigator, 'clipboard', { configurable: true, value: undefined })
}

function mitShare(share: (data: ShareData) => Promise<void>) {
  Object.defineProperty(navigator, 'share', { configurable: true, value: vi.fn(share) })
}

afterEach(() => {
  Reflect.deleteProperty(navigator, 'clipboard')
  Reflect.deleteProperty(navigator, 'share')
  Reflect.deleteProperty(document, 'execCommand')
})

describe('ShareLink', () => {
  it('kopiert über die Zwischenablage und benennt, was darin liegt', async () => {
    mitClipboard(() => Promise.resolve())
    aufbau()

    fireEvent.click(screen.getByRole('button', { name: 'Anmeldelink kopieren' }))

    expect(navigator.clipboard.writeText).toHaveBeenCalledWith(LINK)
    expect(await screen.findByRole('status')).toHaveTextContent('Anmeldelink kopiert.')
  })

  it('nimmt den zweiten Weg, wo es keine Zwischenablage gibt', async () => {
    ohneClipboard()
    const execCommand = vi.fn(() => true)
    Object.defineProperty(document, 'execCommand', { configurable: true, value: execCommand })

    aufbau()
    fireEvent.click(screen.getByRole('button', { name: 'Anmeldelink kopieren' }))

    expect(execCommand).toHaveBeenCalledWith('copy')
    expect(await screen.findByRole('status')).toHaveTextContent('Anmeldelink kopiert.')
    expect(document.querySelector('textarea')).toBeNull()
  })

  it('nimmt den zweiten Weg auch, wenn der erste scheitert', async () => {
    mitClipboard(() => Promise.reject(new Error('kein Fokus')))
    const execCommand = vi.fn(() => true)
    Object.defineProperty(document, 'execCommand', { configurable: true, value: execCommand })

    aufbau()
    fireEvent.click(screen.getByRole('button', { name: 'Anmeldelink kopieren' }))

    await waitFor(() => expect(execCommand).toHaveBeenCalled())
    expect(await screen.findByRole('status')).toHaveTextContent('Anmeldelink kopiert.')
  })

  it('meldet ehrlich, wenn nichts in der Ablage liegt', async () => {
    ohneClipboard()
    Object.defineProperty(document, 'execCommand', { configurable: true, value: () => false })

    aufbau()
    fireEvent.click(screen.getByRole('button', { name: 'Anmeldelink kopieren' }))

    expect(await screen.findByRole('status')).toHaveTextContent(
      'Kopieren: Der Browser hat das Kopieren abgelehnt. Der Link lässt sich von Hand markieren.',
    )
  })

  it('meldet auch ehrlich, wenn der zweite Weg wirft', async () => {
    ohneClipboard()
    Object.defineProperty(document, 'execCommand', {
      configurable: true,
      value: () => {
        throw new Error('nicht erlaubt')
      },
    })

    aufbau()
    fireEvent.click(screen.getByRole('button', { name: 'Anmeldelink kopieren' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Kopieren: Der Browser hat das Kopieren abgelehnt')
  })

  it('zeigt „Teilen" nur, wo das Gerät es kann', () => {
    ohneClipboard()
    aufbau()
    expect(screen.queryByRole('button', { name: 'Teilen' })).not.toBeInTheDocument()
  })

  it('teilt Titel, Text und Adresse', async () => {
    ohneClipboard()
    mitShare(() => Promise.resolve())
    aufbau()

    fireEvent.click(screen.getByRole('button', { name: 'Teilen' }))

    expect(navigator.share).toHaveBeenCalledWith({
      title: 'Clubmeisterschaft 2026',
      text: 'Jetzt melden',
      url: LINK,
    })
  })

  it('schweigt, wenn jemand das Teilen-Blatt zumacht', async () => {
    ohneClipboard()
    mitShare(() => Promise.reject(new DOMException('abgebrochen', 'AbortError')))
    aufbau()

    fireEvent.click(screen.getByRole('button', { name: 'Teilen' }))

    await waitFor(() => expect(navigator.share).toHaveBeenCalled())
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('meldet einen echten Fehler beim Teilen', async () => {
    ohneClipboard()
    mitShare(() => Promise.reject(new Error('nicht erlaubt')))
    aufbau()

    fireEvent.click(screen.getByRole('button', { name: 'Teilen' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Teilen: nicht erlaubt')
  })

  it('nimmt eine eigene Gestalt für den Knopf', () => {
    ohneClipboard()
    aufbau({ className: 'md-btn md-btn--ghost' })

    expect(screen.getByRole('button', { name: 'Anmeldelink kopieren' })).toHaveClass('md-btn--ghost')
  })
})
