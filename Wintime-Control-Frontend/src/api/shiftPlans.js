import apiClient from './client'

export const shiftPlansApi = {
  getRange: (from, to) => apiClient.get('/shift-plan', { params: { from, to } }),
  getLeadCandidates: () => apiClient.get('/shift-plan/lead-candidates'),
  setLead: (date, position, shiftLeadId) =>
    apiClient.put(`/shift-plan/days/${date}/items/${position}/lead`, { shiftLeadId }),
  materializeShift: (date, position) =>
    apiClient.post(`/shift-plan/days/${date}/items/${position}/materialize`)
}
