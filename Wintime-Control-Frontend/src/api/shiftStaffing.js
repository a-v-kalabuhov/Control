import apiClient from './client'

export const shiftStaffingApi = {
  nearest: () => apiClient.get('/shift-staffing/nearest'),
  history: () => apiClient.get('/shift-staffing/history'),
  item: itemId => apiClient.get(`/shift-staffing/items/${itemId}`),
  task: (itemId, taskId) => apiClient.get(`/shift-staffing/items/${itemId}/tasks/${taskId}`),
  operators: () => apiClient.get('/shift-staffing/operators'),
  assignOperator: (itemId, { immId, operatorId, assignedAt = null, unassignedAt = null }) =>
    apiClient.post(`/shift-staffing/items/${itemId}/operators`, {
      immId, operatorId, assignedAt, unassignedAt
    }),
  unassignOperator: (assignmentId, { unassignedAt } = {}) =>
    apiClient.post(
      `/shift-staffing/assignments/${assignmentId}/unassign`,
      unassignedAt ? { unassignedAt } : {}
    ),
  updateOperatorInterval: (assignmentId, { assignedAt, unassignedAt = null }) =>
    apiClient.put(`/shift-staffing/assignments/${assignmentId}/interval`, {
      assignedAt, unassignedAt
    })
}
