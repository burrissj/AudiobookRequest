import { useState } from 'react'
import { useFollows, type FollowTarget } from '../state/useFollows'

interface FollowButtonProps {
  target: FollowTarget
}

/**
 * Follow / Following toggle for an author or series. Following asks one question first -- whether new books
 * should also be requested automatically -- because that is the only choice that has consequences.
 */
export function FollowButton({ target }: FollowButtonProps) {
  const { findFollow, follow, unfollow, setAutoRequest, error } = useFollows()
  const existing = findFollow(target.kind, target.name)
  const [choosing, setChoosing] = useState(false)
  const [auto, setAuto] = useState(false)
  const [working, setWorking] = useState(false)

  const autoLabel =
    target.kind === 'Author' ? 'Request their new books automatically' : "Request the next book automatically after I finish one"

  async function confirm() {
    setWorking(true)
    await follow(target, auto)
    setWorking(false)
    setChoosing(false)
  }

  if (existing) {
    return (
      <div className="follow-box">
        <button onClick={() => unfollow(existing.id)} className="follow-button following">
          ✓ Following
        </button>
        <label className="checkbox-label follow-auto">
          <input type="checkbox" checked={existing.autoRequest} onChange={(e) => setAutoRequest(existing.id, e.target.checked)} />
          {autoLabel}
        </label>
        {error && <p className="error">{error}</p>}
      </div>
    )
  }

  if (choosing) {
    return (
      <div className="follow-box">
        <p className="follow-prompt">
          Follow <strong>{target.name}</strong> and get told about new books.
        </p>
        <label className="checkbox-label follow-auto">
          <input type="checkbox" checked={auto} onChange={(e) => setAuto(e.target.checked)} />
          {autoLabel}
        </label>
        <div className="request-action-group">
          <button className="primary" disabled={working} onClick={confirm}>
            {working ? 'Following…' : 'Follow'}
          </button>
          <button disabled={working} onClick={() => setChoosing(false)}>
            Cancel
          </button>
        </div>
        {error && <p className="error">{error}</p>}
      </div>
    )
  }

  return (
    <div className="follow-box">
      <button className="follow-button" onClick={() => setChoosing(true)}>
        + Follow {target.kind === 'Author' ? 'author' : 'series'}
      </button>
    </div>
  )
}
