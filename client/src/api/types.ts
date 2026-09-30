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
}

export type BookRequestStatus = 'Requested' | 'Searching' | 'Downloading' | 'Processing' | 'Ready' | 'Failed'

export interface BookRequestDto {
  id: number
  chaptarrForeignBookId: string
  title: string
  author: string
  series: string | null
  coverUrl: string | null
  status: BookRequestStatus
  failureReason: string | null
  requestedByUsername: string
  requestedAt: string
  readyAt: string | null
}
