import apiClient from './client'

export const getByItem = itemId => apiClient.get(`/shift-closures/items/${itemId}`)

export const getPending = () => apiClient.get('/shift-closures/pending')

export const saveDraft = (itemId, entries) =>
  apiClient.put(`/shift-closures/items/${itemId}/draft`, { entries })

export const close = (itemId, confirmEarlyClose = false) =>
  apiClient.post(`/shift-closures/items/${itemId}/close`, { confirmEarlyClose })

export const correct = (closureId, reason, entries) =>
  apiClient.patch(`/shift-closures/${closureId}/corrections`, { reason, entries })

export const getCorrections = closureId =>
  apiClient.get(`/shift-closures/${closureId}/corrections`)

export const getBlankPdf = itemId =>
  apiClient.get(`/shift-closures/items/${itemId}/blank-pdf`, { responseType: 'blob' })

export const getClosedPdf = closureId =>
  apiClient.get(`/shift-closures/${closureId}/pdf`, { responseType: 'blob' })
