import { REWIND_CHOICES, SKIP_CHOICES } from '../player/prefs'
import { usePlayer } from '../state/usePlayer'

/** How the player behaves on this device: skip lengths, rewind on resume, auto-play of the next book. */
export function PlayerPrefsForm() {
  const { prefs, setPrefs } = usePlayer()

  return (
    <div className="prefs-form">
      <label>
        Back button
        <select value={prefs.skipBack} onChange={(e) => setPrefs({ skipBack: Number(e.target.value) })}>
          {SKIP_CHOICES.map((s) => (
            <option key={s} value={s}>
              {s} seconds
            </option>
          ))}
        </select>
      </label>
      <label>
        Forward button
        <select value={prefs.skipForward} onChange={(e) => setPrefs({ skipForward: Number(e.target.value) })}>
          {SKIP_CHOICES.map((s) => (
            <option key={s} value={s}>
              {s} seconds
            </option>
          ))}
        </select>
      </label>
      <label>
        Step back when resuming
        <select value={prefs.resumeRewind} onChange={(e) => setPrefs({ resumeRewind: Number(e.target.value) })}>
          {REWIND_CHOICES.map((s) => (
            <option key={s} value={s}>
              {s === 0 ? 'Don’t step back' : `${s} seconds`}
            </option>
          ))}
        </select>
      </label>
      <label className="checkbox-label">
        <input type="checkbox" checked={prefs.autoPlayNext} onChange={(e) => setPrefs({ autoPlayNext: e.target.checked })} />
        Play the next book in a series automatically
      </label>
      <p className="subtitle prefs-note">These apply on this device only.</p>
    </div>
  )
}
