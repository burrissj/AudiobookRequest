export interface SetupStatus {
  setupCompleted: boolean
}

export interface SetupRequest {
  readarrUrl: string
  readarrApiKey: string
  readarrRootFolderPath: string
  readarrQualityProfileId: number
  readarrMetadataProfileId: number
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
