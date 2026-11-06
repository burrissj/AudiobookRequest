import { useCallback, useState } from 'react'

/** "grid" is the plain covers-with-captions list; "shelf" stands the covers on wooden shelves, like a bookcase. */
export type ListenView = 'grid' | 'shelf'

const STORAGE_KEY = 'arq.listenView'

function stored(): ListenView {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'shelf' ? 'shelf' : 'grid'
  } catch {
    return 'grid'
  }
}

/** How the person likes their books laid out on the Listen and Library tabs, remembered on this device. */
export function useListenView(): [ListenView, (view: ListenView) => void] {
  const [view, setViewState] = useState<ListenView>(stored)
  const setView = useCallback((next: ListenView) => {
    setViewState(next)
    try {
      localStorage.setItem(STORAGE_KEY, next)
    } catch {
      // Private window or blocked storage: the choice just lasts until the page is closed.
    }
  }, [])
  return [view, setView]
}
