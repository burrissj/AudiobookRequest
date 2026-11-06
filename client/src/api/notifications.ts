import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useEffect } from 'react'
import type { BookRequestDto } from './types'

export function useRequestUpdates(onUpdate: (request: BookRequestDto) => void, onRemove: (requestId: number) => void) {
  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/notifications')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('requestUpdated', onUpdate)
    connection.on('requestRemoved', onRemove)
    connection.start().catch((err) => console.error('SignalR connection failed', err))

    return () => {
      connection.stop()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])
}
