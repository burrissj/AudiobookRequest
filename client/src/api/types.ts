export interface SetupStatus {
  setupCompleted: boolean
}

export interface SetupRequest {
  chaptarrUrl: string
  chaptarrApiKey: string
  chaptarrRootFolderPath: string
  jellyfinUrl: string
  jellyfinApiKey: string
  stagingPath: string
  libraryPath: string
  scanIntervalMinutes: number
  originalRetentionDays: number
  namingPattern: string
  chaptarrFilePathPrefix: string | null
  localFilePathPrefix: string | null
  enableRenaming: boolean
  enableConversion: boolean
  enableMoving: boolean
  requireApproval: boolean
  maxRequestsPerUserPerWeek: number
  minFreeDiskGb: number
  wishlistRetryHours: number
  notifyOnReady: boolean
  notifyOnFailed: boolean
  notifyOnPendingApproval: boolean
  notifyDiscordWebhookUrl: string | null
  notifyTelegramBotToken: string | null
  notifyTelegramChatId: string | null
  notifyNtfyUrl: string | null
  notifyWebhookUrl: string | null
  smtpHost: string | null
  smtpPort: number
  smtpUseSsl: boolean
  smtpUsername: string | null
  smtpPassword: string | null
  smtpFrom: string | null
  smtpTo: string | null
  notifyOnNewRelease: boolean
  autoFixImports: boolean
  backupRetentionCount: number
  jellyfinPublicUrl: string | null
}

export interface CurrentUser {
  username: string
  role: 'User' | 'Admin'
}

export interface BookSearchResult {
  foreignBookId: string
  title: string
  authorName: string
  seriesTitle: string | null
  overview: string | null
  coverUrl: string | null
  releaseDate: string | null
  ratingValue: number | null
  ratingVotes: number | null
  alreadyInLibrary: boolean
  /** Title of a likely different edition already in the library -- a warning, not a block. */
  similarInLibraryTitle: string | null
  /** Only present when a narrator/length filter made the server look it up. */
  runtimeMinutes: number | null
  narrators: string[] | null
}

export type BookRequestStatus =
  | 'Requested'
  | 'Searching'
  | 'Snatched'
  | 'Downloading'
  | 'Processing'
  | 'Ready'
  | 'Failed'
  | 'PendingApproval'
  | 'Denied'
  | 'Upcoming'

export interface BookRequestDto {
  id: number
  chaptarrForeignBookId: string
  title: string
  author: string
  series: string | null
  coverUrl: string | null
  status: BookRequestStatus
  failureReason: string | null
  downloadProgressPercent: number | null
  downloadSizeBytes: number | null
  downloadSizeLeftBytes: number | null
  downloadClientName: string | null
  requestedByUsername: string
  requestedAt: string
  snatchedAt: string | null
  readyAt: string | null
  /** What Chaptarr (or this app) says is wrong while the request is stuck. */
  diagnostic: string | null
  wishlisted: boolean
  /** When the book comes out, if known. An Upcoming request waits for it. */
  releaseDate: string | null
  /** For an Upcoming request: when it will next be searched for. */
  nextSearchAt: string | null
}

export interface RecommendationDto {
  foreignBookId: string
  title: string
  authorName: string
  seriesTitle: string | null
  coverUrl: string | null
  reason: string
  overview: string | null
  releaseDate: string | null
  ratingValue: number | null
  ratingVotes: number | null
}

export interface AudiobookMetadata {
  runtimeMinutes: number | null
  narrators: string[]
  genres: string[]
  description: string | null
  formatType: string | null
  language: string | null
  publisherName: string | null
  seriesTitle: string | null
  seriesAsin: string | null
  /** Set when this book is actually a bundle/omnibus (e.g. 3 for "The Darkwood Trilogy"), not a single book. */
  bundleBookCount: number | null
}

/** Shared shape for anything that can open the book detail page: BookSearchResult and RecommendationDto both satisfy this. */
export interface DetailableBook {
  foreignBookId: string
  title: string
  authorName: string
  seriesTitle: string | null
  coverUrl: string | null
  overview: string | null
  releaseDate: string | null
  ratingValue: number | null
  ratingVotes: number | null
  /** Recommendations never carry this (already-owned books are excluded from them at the source), so it's optional. */
  alreadyInLibrary?: boolean
  similarInLibraryTitle?: string | null
}

export interface SeriesMemberStatus {
  title: string
  number: number
  state: 'owned' | 'requested' | 'missing'
}

export interface SeriesStatus {
  seriesTitle: string
  total: number
  owned: number
  requested: number
  members: SeriesMemberStatus[]
}

export interface ExpandSeriesResponse {
  created: BookRequestDto[]
  skippedByLimit: number
}

export interface HealthCheck {
  name: string
  status: 'ok' | 'warn' | 'fail' | 'skip' | 'info'
  detail: string
}

export interface JobRun {
  name: string
  lastRunAt: string | null
  lastSucceeded: boolean | null
  lastError: string | null
  lastSuccessAt: string | null
}

export interface LibraryStatus {
  bookCount: number
  lastRefreshedAt: string | null
  jellyfinConfigured: boolean
}

export interface StaleRequest {
  id: number
  title: string
  author: string
  requestedBy: string
  readyAt: string | null
}

export interface StaleScan {
  finishedChecked: number
  jellyfinBooks: number
  /** Why nothing could be checked safely (Jellyfin not set up, or reporting an empty library). */
  blocked: string | null
  missing: StaleRequest[]
}

export interface RemoveStaleResult {
  removed: number
  stillInJellyfin: number
}

export interface HealthReport {
  generatedAt: string
  checks: HealthCheck[]
  jobs: JobRun[]
  requestCounts: Record<string, number>
  stuckRequests: number
  library: LibraryStatus
  checklist: ChecklistItem[]
}

export interface LogEntry {
  level: string
  text: string
}

export interface LogView {
  file: string
  files: string[]
  entries: LogEntry[]
  truncated: boolean
}

export interface ChannelResult {
  channel: string
  success: boolean
  error: string | null
}

export interface NotificationTestResponse {
  results: ChannelResult[]
  anyConfigured: boolean
}

/** A catalog book on a browse screen. It has no Chaptarr id yet -- that is looked up when it is opened. */
export interface BrowseItem {
  asin: string
  title: string
  author: string
  coverUrl: string
  runtimeMinutes: number | null
  rating: number | null
  seriesTitle: string | null
  alreadyInLibrary: boolean
  similarInLibraryTitle: string | null
  alreadyRequested: boolean
  /** yyyy-MM-dd; in the future for announced books. */
  releaseDate: string | null
}

export type CategoryLayoutName = 'grid' | 'tilted' | 'shelf'

export interface CategoryInfo {
  id: string
  title: string
  description: string
  layout: CategoryLayoutName
  /** True for keyword-driven categories, where "Best match" is a meaningful sort. */
  hasKeywordSort: boolean
}

export interface CategorySection {
  category: CategoryInfo
  items: BrowseItem[]
  totalTitles: number
}

export interface CategoryPage {
  category: CategoryInfo
  items: BrowseItem[]
  totalTitles: number
  page: number
  hasMore: boolean
}

export interface CategoryListEntry {
  category: CategoryInfo
  tags: string[]
}

export interface ImportFixFile {
  fileName: string
  sizeBytes: number
  matchedBookId: number | null
  matchedTitle: string | null
  rejections: string[]
}

export interface ImportFixPreview {
  canFix: boolean
  reason: string | null
  targetTitle: string
  files: ImportFixFile[]
  /** Whether the automatic-import rules would have handled this on their own. */
  autoFixable: boolean
  autoFixNote: string | null
}

export interface ImportFixResult {
  outcome: 'completed' | 'started' | 'failed'
  message: string
}

export interface FollowDto {
  id: number
  kind: 'Author' | 'Series'
  name: string
  seriesAsin: string | null
  autoRequest: boolean
  createdAt: string
  lastCheckedAt: string | null
}

export interface FollowFindDto {
  id: number
  followId: number
  followName: string
  item: BrowseItem
  foundAt: string
}

export interface BackupInfo {
  fileName: string
  sizeBytes: number
  createdAt: string
}

export interface BackupList {
  backups: BackupInfo[]
  retentionCount: number
}

export interface ChangelogEntry {
  version: string
  date: string
  items: string[]
}

export interface VersionInfo {
  version: string
  changelog: ChangelogEntry[]
}

export interface AppConfig {
  /** Where "Open Jellyfin" goes, or null when Jellyfin isn't configured. Never contains a key. */
  jellyfinUrl: string | null
  iosAppUrl: string
  androidAppUrl: string
}

export interface JellyfinLink {
  url: string
  /** True when the link goes straight to this book, false when it's Jellyfin's home page. */
  exact: boolean
  /** The book's Jellyfin id when it was found, so it can be played in the app. */
  itemId: string | null
}

export interface AccountNotifications {
  ntfyTopic: string | null
  telegramChatId: string | null
  email: string | null
  onReady: boolean
  onFailed: boolean
  onNewRelease: boolean
  available: { ntfy: boolean; telegram: boolean; email: boolean; ntfyServer: string }
}

export interface ChecklistItem {
  key: string
  title: string
  status: 'done' | 'todo' | 'optional'
  detail: string
  action: string | null
}

export interface ListenItem {
  id: string
  title: string
  author: string | null
  durationSeconds: number
  positionSeconds: number
  played: boolean
  hasCover: boolean
  /** The signed-in person's own 1-5 star rating, if they gave one. */
  myRating?: number | null
  /** The series the book's title says it belongs to, and its number in it. */
  series?: string | null
  seriesNumber?: number | null
}

/** One pile of the Library: an author's books or a series. `key` "-" is the pile of books with no author / no series. */
export interface ListenGroup {
  key: string
  name: string
  count: number
  finished: number
  /** Up to three books of the pile that have a cover. */
  coverIds: string[]
  /** For a series, who wrote it. */
  by?: string | null
}

export interface ListenGroups {
  groups: ListenGroup[]
  /** How many books haven't been looked up on Audible for their series yet (they are grouped by title meanwhile). */
  pending: number
}

export interface ListenLibrary {
  items: ListenItem[]
  total: number
}

export interface ListenChapter {
  name: string
  startSeconds: number
}

export interface ListenDetail extends ListenItem {
  mediaSourceId: string | null
  chapters: ListenChapter[]
  canPlay: boolean
  cantPlayReason: string | null
  /** "direct" plays the original file; "hls" is converted to AAC by Jellyfin because browsers can't play the original. */
  streamMode: 'direct' | 'hls'
  /** Only unconverted books can be saved for offline listening. */
  canDownload: boolean
  /** Jellyfin's own description of the book, when it has one. */
  overview?: string | null
}

/** An admin's correction to a book: fields left null are left to Jellyfin, Audible and the title. */
export interface BookOverride {
  author: string | null
  series: string | null
  seriesNumber: number | null
  /** The book is in no series, whatever Audible or its title say. */
  notInSeries: boolean
}

/** What the edit form needs: the correction saved for the book (if any) and what Audible said about it. */
export interface BookMetadataEdit {
  override: BookOverride | null
  audible: { checked: boolean; series: string | null; number: number | null }
}

export interface ListenDayPoint {
  day: string
  seconds: number
}

export interface ListenStats {
  todaySeconds: number
  last7DaysSeconds: number
  last30DaysSeconds: number
  thisYearSeconds: number
  allTimeSeconds: number
  timeSavedSeconds: number
  currentStreakDays: number
  longestStreakDays: number
  activeDays: number
  averageSecondsPerActiveDay: number
  last30Days: ListenDayPoint[]
  topBooks: { itemId: string; title: string; author: string | null; seconds: number }[]
  topAuthors: { author: string; seconds: number }[]
  booksFinishedThisYear: number | null
  booksFinishedTotal: number | null
}

export interface Bookmark {
  id: number
  positionSeconds: number
  note: string | null
  label: string | null
  createdAt: string
  /** Set while a bookmark made offline is still waiting to reach the server. */
  pendingClientId?: string
}

export interface PersonalBook {
  rating: number | null
  tags: string[]
  bookmarks: Bookmark[]
}

export interface TagCount {
  tag: string
  count: number
}

export interface ImportFolder {
  folder: string | null
  libraryPath: string | null
  namingPattern: string
  /** Why importing can't work yet (setup unfinished), if so. */
  problem: string | null
}

export interface ImportCandidate {
  id: string
  kind: 'Folder' | 'File'
  name: string
  relativePath: string
  fileCount: number
  totalBytes: number
  imported: boolean
  importedAs: string | null
}

export interface ImportScan {
  folder: string | null
  candidates: ImportCandidate[]
}

export interface ImportFile {
  name: string
  bytes: number
  seconds: number
  codec: string | null
  problem: string | null
}

export interface ImportMatch {
  asin: string
  title: string
  author: string
  series: string | null
  seriesNumber: string | null
  coverUrl: string | null
  runtimeMinutes: number | null
  narrators: string[]
  score: number
  lengthNote: string | null
}

export interface ImportIdentify {
  id: string
  name: string
  files: ImportFile[]
  totalSeconds: number
  guessTitle: string
  guessAuthor: string | null
  query: string
  matches: ImportMatch[]
  problems: string[]
}

export interface ImportPreview {
  relativePath: string
  fullPath: string
  fileExists: boolean
  alreadyInJellyfin: boolean
}

export interface ImportResult {
  finalPath: string
  movedOriginals: boolean
  note: string | null
}

export interface ImportJob {
  jobId: string
  candidateId: string
  name: string
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled'
  stage: string
  error: string | null
  result: ImportResult | null
  queuedAt: string
  startedAt: string | null
  finishedAt: string | null
}
