import { Link } from 'react-router-dom'
import type { BrowseItem, CategorySection } from '../api/types'
import { useCatalogActions } from '../hooks/useCatalogActions'
import { badgeFor, badgeToneFor, roundedCount } from '../lib/catalog'
import { CoverCard } from './CoverCard'

type Actions = ReturnType<typeof useCatalogActions>

// A stable per-category tilt so the same category always looks the same, but different categories lean differently.
function tiltFor(id: string): number {
  let hash = 0
  for (const ch of id) hash = (hash * 31 + ch.charCodeAt(0)) | 0
  const angle = 28 + (Math.abs(hash) % 14)
  return hash % 2 === 0 ? -angle : angle
}

interface CategorySectionViewProps {
  section: CategorySection
  actions: Actions
  onDismiss: () => void
}

/** One category on the For You page: serif heading, blurb, "see all" link, then covers laid out per the category's layout. */
export function CategorySectionView({ section, actions, onDismiss }: CategorySectionViewProps) {
  const { category, items, totalTitles } = section
  const link = `/category/${category.id}`

  function card(item: BrowseItem, mode: 'grid' | 'tilted' | 'shelf') {
    return (
      <CoverCard
        key={item.asin}
        coverUrl={item.coverUrl}
        title={item.title}
        author={item.author}
        runtimeMinutes={item.runtimeMinutes}
        badge={badgeFor(item, actions.isRequested(item))}
        badgeTone={badgeToneFor(item, actions.isRequested(item))}
        showText={mode === 'shelf'}
        hoursPlacement={mode === 'tilted' ? 'overlay' : mode === 'grid' ? 'below' : 'none'}
        busy={actions.busyAsin === item.asin}
        onClick={() => actions.open(item)}
      />
    )
  }

  return (
    <section className="category-section">
      <div className="category-heading">
        <h2>
          <Link to={link}>{category.title}</Link>
        </h2>
        <button className="dismiss-button" onClick={onDismiss} aria-label={`Hide ${category.title}`} title="Hide this category">
          ✕
        </button>
      </div>
      <p className="category-description">{category.description}</p>
      <Link className="category-more" to={link}>
        See over {roundedCount(totalTitles)} titles <span aria-hidden="true">›</span>
      </Link>

      {category.layout === 'grid' && <div className="cover-grid">{items.slice(0, 6).map((i) => card(i, 'grid'))}</div>}

      {category.layout === 'shelf' && <div className="cover-shelf">{items.map((i) => card(i, 'shelf'))}</div>}

      {category.layout === 'tilted' && (
        <div className="tilted-wrap">
          <div className="tilted-mosaic" style={{ ['--tilt' as string]: `${tiltFor(category.id)}deg` }}>
            {items.slice(0, 12).map((i) => card(i, 'tilted'))}
          </div>
        </div>
      )}
    </section>
  )
}
