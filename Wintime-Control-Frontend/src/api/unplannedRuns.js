import apiClient from './client'

export const unplannedRunsApi = {
  getList(params) {
    return apiClient.get('/unplanned-runs', { params })
  },
  getCandidates(id) {
    return apiClient.get(`/unplanned-runs/${id}/candidates`)
  },
  assign(id, taskId) {
    return apiClient.post(`/unplanned-runs/${id}/assign`, { taskId })
  }
}
