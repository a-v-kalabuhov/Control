import apiClient from './client'

export const nomenclatureImportApi = {
  upload(file, skipInvalidRows = false) {
    const data = new FormData()
    data.append('file', file)
    data.append('skipInvalidRows', String(skipInvalidRows))
    return apiClient.post('/nomenclature-import', data, {
      headers: { 'Content-Type': undefined },
      timeout: 120000
    })
  }
}
