/**
 * Rendern mit dem, was eine Ansicht voraussetzt.
 *
 * Die Bildschirme hängen an zwei Zusammenhängen: dem Arbeitsbereich
 * (gewähltes Turnier, Zeitzone) und den Meldungen. Beide von Hand in jedem
 * Test aufzubauen war der erste Versuch — er endete darin, dass die Hälfte der
 * Tests eine andere Zeitzone setzte als die andere.
 */

import { render, type RenderOptions, type RenderResult } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactElement, ReactNode } from 'react'
import { ToastProvider } from '../hooks/useToast'
import { WorkspaceContext, type Workspace } from '../state/WorkspaceContext'
import * as fx from './fixtures'

export const noopReload = () => Promise.resolve()

export function workspace(over: Partial<Workspace> = {}): Workspace {
  return {
    me: fx.meResponse(),
    tournaments: [fx.tournamentSummary()],
    tournament: fx.tournamentDetail(),
    timeZone: 'Europe/Vienna',
    selectTournament: () => {},
    reloadTournament: noopReload,
    loading: false,
    ...over,
  }
}

interface Options extends Omit<RenderOptions, 'wrapper'> {
  /** Der Arbeitsbereich. `null` heißt: ohne — wie in der öffentlichen Ansicht. */
  workspace?: Workspace | null
}

export function renderWithProviders(ui: ReactElement, options: Options = {}): RenderResult {
  const { workspace: ws = workspace(), ...rest } = options

  function Wrapper({ children }: { children: ReactNode }) {
    const inner = <ToastProvider>{children}</ToastProvider>
    return ws ? (
      <WorkspaceContext.Provider value={ws}>{inner}</WorkspaceContext.Provider>
    ) : (
      inner
    )
  }

  return render(ui, { wrapper: Wrapper, ...rest })
}

/**
 * Ein Benutzer ohne künstliche Verzögerung.
 *
 * `userEvent` wartet zwischen Tastenanschlägen; bei Formularen mit vielen
 * Feldern summiert sich das auf Sekunden je Test.
 */
export function user() {
  return userEvent.setup({ delay: null })
}
