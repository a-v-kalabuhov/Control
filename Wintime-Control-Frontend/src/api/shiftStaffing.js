import apiClient from './client'

export const shiftStaffingApi = {
  nearest: () => apiClient.get('/shift-staffing/nearest'),
  history: () => apiClient.get('/shift-staffing/history'),
  item: itemId => apiClient.get(`/shift-staffing/items/${itemId}`),
  task: (itemId, taskId) => apiClient.get(`/shift-staffing/items/${itemId}/tasks/${taskId}`),
  operators: () => apiClient.get('/shift-staffing/operators'),
  assign: (itemId, immId, operatorId) =>
    apiClient.post(`/shift-staffing/items/${itemId}/operators`, { immId, operatorId }),
  unassign: assignmentId =>
    apiClient.post(`/shift-staffing/assignments/${assignmentId}/unassign`)
}
