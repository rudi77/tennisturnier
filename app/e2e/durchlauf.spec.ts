/**
 * Ein Turnier von der Registrierung bis zum Endstand.
 *
 * Vier Menschen, die es vorher nicht gab: eine legt „Jeder gegen jeden" an,
 * spielt selbst mit, die anderen drei kommen über den geteilten Link. Danach
 * wird ausgelost, gespielt und jedes der sechs Spiele eingetragen.
 *
 * Kein Ausschnitt, sondern der ganze Weg — und deshalb der Lauf, der auffällt,
 * wenn zwischen Aussteller, Beitritt, Auslosung und Ergebniserfassung etwas
 * nicht zusammenpasst. Er läuft nur auf Zuruf, weil vier Registrierungen über
 * die Maske des Ausstellers ihre Zeit brauchen:
 *
 *     MATCHDAY_DURCHLAUF=1 npx playwright test durchlauf
 */

import { expect, test } from './support/fixtures'
import type { Browser, Page } from '@playwright/test'

const BILDER = 'test-results/durchlauf'

/** Ein frischer Mensch: eigener Kontext, eigenes Konto beim Aussteller. */
async function neuerMensch(
  browser: Browser,
  vorname: string,
  nachname: string,
  ziel: string,
): Promise<Page> {
  const kontext = await browser.newContext({ viewport: { width: 1280, height: 900 } })
  const seite = await kontext.newPage()

  await seite.goto(ziel)
  await seite.waitForURL(/realms\/tennisturnier/)
  await seite.getByRole('link', { name: /Register/i }).click()

  const marke = `${vorname}.${nachname}.${Date.now().toString(36)}`.toLowerCase()

  await seite.locator('#firstName').fill(vorname)
  await seite.locator('#lastName').fill(nachname)
  await seite.locator('#email').fill(`${marke}@example.invalid`)
  await seite.locator('#password').fill('Str3ng-geheim!')
  await seite.locator('#password-confirm').fill('Str3ng-geheim!')
  await seite.locator('input[type="submit"]').click()

  await seite.waitForURL(/localhost:5001/)
  return seite
}

/** Ein Ergebnis über den Stepper eintragen. */
async function ergebnis(seite: Page, saetze: [number, number][]): Promise<void> {
  const maske = seite.getByRole('dialog', { name: 'Ergebnis erfassen' })
  await expect(maske).toBeVisible()

  for (const [nummer, [oben, unten]] of saetze.entries()) {
    // Die letzte Spalte heißt im Standardformat nicht „Satz 3", sondern
    // „M-Tiebreak" — der Champions-Tiebreak steht statt des dritten Satzes.
    const satz = maske.getByRole('button', { name: new RegExp(`, Satz ${nummer + 1} erhöhen$`) })
    const knoepfe =
      (await satz.count()) > 0
        ? await satz.all()
        : await maske.getByRole('button', { name: /, M-Tiebreak erhöhen$/ }).all()

    for (let i = 0; i < oben; i++) await knoepfe[0]!.click()
    for (let i = 0; i < unten; i++) await knoepfe[1]!.click()
  }

  await maske.getByRole('button', { name: 'Speichern & propagieren' }).click()
  await expect(maske).toBeHidden()
}

test('vier Menschen spielen jeder gegen jeden — von der Registrierung bis zum Endstand', async ({
  browser,
}) => {
  const name = `Sommerturnier ${Date.now().toString(36)}`

  // --- 1. Die Anlegerin registriert sich ------------------------------------
  const anna = await neuerMensch(browser, 'Anna', 'Berger', '/?screen=create')
  await expect(anna.getByRole('heading', { name: 'Turnier anlegen' })).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/01-anlegen-leer.png`, fullPage: true })

  // --- 2. Turnier anlegen ----------------------------------------------------
  await anna.getByLabel('Name', { exact: true }).fill(name)
  await anna.getByLabel('Anlage', { exact: true }).fill('TC Teisendorf')
  await anna.getByRole('button', { name: /Jeder gegen jeden/ }).click()

  // Plätze und Ort stehen in der Lade — zwei Plätze sind die Vorgabe.
  await anna.getByText(/2 Plätze · /).click()
  await anna.getByLabel('Ort', { exact: true }).fill('Teisendorf')
  await anna.screenshot({ path: `${BILDER}/02-anlegen-gefuellt.png`, fullPage: true })

  await anna.getByRole('button', { name: 'Turnier anlegen', exact: true }).click()

  await expect(anna.getByRole('heading', { name: 'Ablauf' })).toBeVisible()
  await expect(anna.locator('.md-appbar__name')).toHaveText(name)
  await anna.screenshot({ path: `${BILDER}/03-ablauf-frisch.png`, fullPage: true })

  // --- 3. Meldung öffnen und den Link mitnehmen ------------------------------
  await anna.getByRole('button', { name: 'Meldung öffnen' }).click()

  const feld = anna.getByLabel('Anmeldelink')
  await expect(feld).toBeVisible()
  const link = await feld.inputValue()
  await anna.screenshot({ path: `${BILDER}/04-meldung-offen.png`, fullPage: true })

  // --- 4. Die drei anderen kommen über den Link ------------------------------
  const gaeste: Page[] = []

  for (const [vorname, nachname] of [
    ['Bea', 'Christl'],
    ['Carla', 'Dorn'],
    ['Dora', 'Egger'],
  ] as const) {
    const seite = await neuerMensch(browser, vorname, nachname, link)

    await expect(seite.getByRole('button', { name: 'Melden und beitreten' })).toBeVisible()
    if (gaeste.length === 0) {
      await seite.screenshot({ path: `${BILDER}/05-beitritt.png`, fullPage: true })
    }

    await seite.getByRole('button', { name: 'Melden und beitreten' }).click()
    await expect(seite.getByText('Du bist dabei')).toBeVisible()

    if (gaeste.length === 0) {
      await seite.screenshot({ path: `${BILDER}/06-beigetreten.png`, fullPage: true })
    }

    gaeste.push(seite)
  }

  // --- 5. Die Anlegerin meldet sich selbst -----------------------------------
  // Über den Beitrittslink geht das nicht: sie gehört schon dazu. Also über die
  // Meldung im Draw, wie bei jedem anderen Namen auch.
  await anna.getByRole('button', { name: 'Draw & Bracket' }).click()

  await anna.getByPlaceholder('Vorname').fill('Anna')
  await anna.getByPlaceholder('Nachname').fill('Berger')
  await anna.getByRole('button', { name: 'Neu', exact: true }).click()
  await anna.getByRole('button', { name: 'Melden', exact: true }).click()

  await expect(anna.getByText('Berger, Anna')).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/07-selbst-gemeldet.png`, fullPage: true })

  // --- 6. Die drei Meldungen annehmen ----------------------------------------
  await anna.getByRole('button', { name: 'Meldungen', exact: true }).click()
  await expect(anna.getByRole('heading', { name: 'Meldungen' })).toBeVisible()

  for (const wer of ['Christl, Bea', 'Dorn, Carla', 'Egger, Dora']) {
    const zeile = anna.locator('.md-entry').filter({ hasText: wer })
    await zeile.getByRole('button', { name: 'Annehmen' }).click()
    await expect(zeile.getByRole('button', { name: 'Annehmen' })).toBeDisabled()
  }

  await anna.screenshot({ path: `${BILDER}/08-meldungen.png`, fullPage: true })

  // --- 7. Meldeschluss, auslosen, starten ------------------------------------
  await anna.getByRole('button', { name: 'Ablauf', exact: true }).click()
  await anna.getByRole('button', { name: 'Meldung schließen' }).click()
  await expect(anna.getByRole('button', { name: 'Auslosen' })).toBeVisible()

  await anna.getByRole('button', { name: 'Auslosen' }).click()
  await expect(anna.getByRole('button', { name: 'Turnier starten' })).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/09-ausgelost.png`, fullPage: true })

  await anna.getByRole('button', { name: 'Turnier starten' }).click()

  // --- 8. Alle sechs Partien eintragen ---------------------------------------
  await anna.getByRole('button', { name: 'Draw & Bracket' }).click()

  const karten = anna.locator('.md-bracket__match--clickable')

  // Jeder gegen jeden mit vier Meldungen: sechs Partien, und alle sind von
  // Anfang an spielbar — es hängt keine an einem Sieger. Über den Index und
  // nicht über „die erste offene": eine entschiedene Partie bleibt anklickbar,
  // damit sie sich korrigieren lässt, und „die erste" wäre immer dieselbe.
  await expect(karten).toHaveCount(6)
  await anna.screenshot({ path: `${BILDER}/10-spielplan.png`, fullPage: true })

  const ergebnisse: [number, number][][] = [
    [
      [6, 3],
      [6, 4],
    ],
    [
      [2, 6],
      [5, 7],
    ],
    [
      [7, 5],
      [6, 4],
    ],
    [
      [6, 0],
      [6, 2],
    ],
    [
      [4, 6],
      [3, 6],
    ],
    [
      [6, 4],
      [6, 2],
    ],
  ]

  for (const [nummer, saetze] of ergebnisse.entries()) {
    await karten.nth(nummer).click()
    await ergebnis(anna, saetze)
  }

  // --- 9. Der Endstand --------------------------------------------------------
  await anna.getByRole('button', { name: 'Ablauf', exact: true }).click()
  await expect(anna.getByText('Alle Partien sind entschieden')).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/11-fertig.png`, fullPage: true })

  await anna.getByRole('button', { name: 'Endstand ansehen' }).click()
  await anna.screenshot({ path: `${BILDER}/12-endstand.png`, fullPage: true })

  // Bei „Jeder gegen jeden" ist die Tabelle das Ergebnis, nicht der Baum.
  await anna.getByRole('button', { name: 'Kompakte Rundenspalten' }).click()
  await anna.screenshot({ path: `${BILDER}/12b-rundenspalten.png`, fullPage: true })

  await anna.getByRole('button', { name: 'Live-Ansicht' }).click()
  await expect(anna.getByRole('heading', { name: 'Live-Ansicht' })).toBeVisible()

  await anna.getByRole('button', { name: 'Tabellen' }).click()
  await expect(anna.getByText(/Berger, Anna/).first()).toBeVisible()
  await anna.screenshot({ path: `${BILDER}/12c-tabelle.png`, fullPage: true })

  // Und aus Sicht einer Mitspielerin: sie sieht das Turnier, führt es aber nicht.
  const bea = gaeste[0]!
  await bea.goto('/?screen=draw')
  await expect(bea.getByRole('heading', { name: 'Draw & Bracket' })).toBeVisible()
  await bea.screenshot({ path: `${BILDER}/13-aus-sicht-mitspielerin.png`, fullPage: true })

  // --- 10. Und der Zustand steht in der Kopfleiste ----------------------------
  // Geprüft wird er dort und nicht über den Testklienten: der ist eine andere
  // Turnierleitung, und ein fremdes Turnier sieht sie nicht — genau das ist der
  // Query-Filter aus ADR-0004, und dass er hier greift, ist richtig.
  await expect(anna.locator('.md-appbar')).toContainText('abgeschlossen')

  for (const seite of [anna, ...gaeste]) await seite.context().close()
})
